using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class SkAgentRouter : IAgentRouter
{
    private readonly KernelFactory          _kernelFactory;
    private readonly ILogger<SkAgentRouter> _log;

    private const string RoutingSystemPrompt = """
        You are an orchestrator for a team of expert agents.
        Each agent has a name and description of what document or data they know.

        Available agents:
        {AGENT_LIST}

        {ATTACHMENT_CONTEXT}

        The user will ask a question. Your ONLY job is to decide which agents
        can help answer it. Respond with ONLY a valid JSON array of objects.
        Each object must have:
          "id"         - the agent ID string (from the list above)
          "confidence" - a float from 0.0 (not relevant) to 1.0 (highly relevant)
          "reason"     - a very brief reason (one short phrase, optional)

        Example:
        [{"id":"tax-guide","confidence":0.95,"reason":"Direct tax question"},{"id":"hr-policy","confidence":0.4,"reason":"May cover benefits"}]

        If no agent is relevant respond with an empty array: []
        Do NOT include any text outside the JSON array.
        """;

    private const string SynthesisSystemPrompt = """
        You are a helpful assistant synthesizing information from multiple expert agents.
        Below are responses from agents who reviewed relevant documents or data.
        Provide a clear, accurate, and concise answer to the user's question
        based solely on these responses. Use markdown formatting where helpful
        (headers, bullet points, code blocks). If the agents did not provide enough
        information, say so clearly.
        """;

    public SkAgentRouter(KernelFactory kernelFactory, ILogger<SkAgentRouter> log)
    {
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    // ── SelectAgentsAsync ────────────────────────────────────────────────────────
    // Kept as a raw IChatCompletionService call: the routing LLM call must return
    // clean JSON and must NOT invoke any KernelFunction tools. Wrapping it in a
    // ChatCompletionAgent would add unnecessary overhead and risk tool calls
    // corrupting the JSON output.
    public async Task<IReadOnlyList<AgentSelection>> SelectAgentsAsync(
        string                   userQuestion,
        IReadOnlyList<AgentInfo> available,
        AttachedDocument?        attachment = null,
        CancellationToken        ct         = default)
    {
        _log.LogDebug(
            "Routing question (length={Len}) across {Count} agents. HasAttachment={Has}.",
            userQuestion.Length, available.Count, attachment is not null);

        var agentList = string.Join("\n", available.Select(a =>
            $"  id: \"{a.Id}\"  name: \"{a.Name}\"  description: \"{a.Description}\""));

        var attachmentContext = BuildAttachmentContext(attachment);

        var systemPrompt = RoutingSystemPrompt
            .Replace("{AGENT_LIST}", agentList)
            .Replace("{ATTACHMENT_CONTEXT}", attachmentContext);

        _log.LogTrace("Routing system prompt:\n{Prompt}", systemPrompt);

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userQuestion);

        var sw  = Stopwatch.StartNew();
        var raw = "[]";

        try
        {
            var result = await RetryHelper.ExecuteAsync(
                async ck => await chatService.GetChatMessageContentAsync(history, cancellationToken: ck),
                _log, "routing", maxAttempts: 3, ct);
            raw = result.Content?.Trim() ?? "[]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Routing LLM call failed after retries. Returning no agents.");
            return [];
        }

        sw.Stop();
        _log.LogDebug("Routing LLM response in {Ms}ms: {Raw}", sw.ElapsedMilliseconds, raw);

        try
        {
            var elements   = JsonSerializer.Deserialize<List<JsonElement>>(raw) ?? [];
            var selections = new List<AgentSelection>(elements.Count);

            foreach (var elem in elements)
            {
                var id = elem.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var confidence = elem.TryGetProperty("confidence", out var confElem)
                    ? confElem.GetDouble()
                    : 1.0;
                var reason = elem.TryGetProperty("reason", out var reasonElem)
                    ? reasonElem.GetString()
                    : null;

                selections.Add(new AgentSelection(id, Math.Clamp(confidence, 0.0, 1.0), reason));
            }

            _log.LogInformation(
                "Router selected {Count} agent(s): {Summary}",
                selections.Count,
                string.Join(", ", selections.Select(s => $"{s.AgentId}({s.Confidence:P0})")));

            return selections;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex,
                "Router returned non-JSON output. Raw='{Raw}'. Falling back to no agents.", raw);
            return [];
        }
    }

    // ── SynthesizeAsync ──────────────────────────────────────────────────────────
    // Uses ChatCompletionAgent.InvokeStreamingAsync so the Agent Framework manages
    // the chat turn, execution settings, and KernelFunction dispatch.
    // Prior conversation turns are passed as messages so the LLM has multi-turn context
    // without requiring a persistent AgentThread across Blazor circuit lifetime.
    public async IAsyncEnumerable<string> SynthesizeAsync(
        string                           userQuestion,
        IReadOnlyList<AgentResponse>     agentResponses,
        IReadOnlyList<ConversationTurn>? history    = null,
        AttachedDocument?                attachment = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _log.LogDebug(
            "Synthesizing from {Count} agent response(s). HistoryTurns={Turns}, HasAttachment={Has}.",
            agentResponses.Count, history?.Count ?? 0, attachment is not null);

        var kernel     = _kernelFactory.Create();
        var chatAgent  = AgentKernelFactory.Create("Synthesizer", SynthesisSystemPrompt, kernel, enableFunctions: true);

        // Build the ordered message list: prior turns first, then the current synthesis request.
        // Passing them all as the 'messages' argument means the agent adds them to a fresh thread
        // in order, giving the LLM full multi-turn context in a single InvokeStreamingAsync call.
        var messages = BuildMessages(userQuestion, agentResponses, history, attachment);

        int chunkCount = 0;
        var sw         = Stopwatch.StartNew();

        await foreach (var chunk in chatAgent.InvokeStreamingAsync(
                           messages, thread: null, options: null, ct))
        {
            var text = chunk.Message.Content;
            if (!string.IsNullOrEmpty(text))
            {
                chunkCount++;
                yield return text;
            }
        }

        _log.LogInformation(
            "Synthesis complete. StreamedChunks={Chunks}, Elapsed={Ms}ms",
            chunkCount, sw.ElapsedMilliseconds);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<ChatMessageContent> BuildMessages(
        string                           userQuestion,
        IReadOnlyList<AgentResponse>     agentResponses,
        IReadOnlyList<ConversationTurn>? history,
        AttachedDocument?                attachment)
    {
        var messages = new List<ChatMessageContent>();

        // Prepend prior conversation turns for multi-turn context
        if (history is { Count: > 0 })
        {
            foreach (var turn in history)
            {
                messages.Add(turn.Role == "user"
                    ? new ChatMessageContent(AuthorRole.User,      turn.Content)
                    : new ChatMessageContent(AuthorRole.Assistant, turn.Content));
            }
        }

        var context = string.Join("\n\n", agentResponses.Select(r =>
            $"--- {r.Agent.Name} ---\n{r.Content}"));

        var attachmentNote = attachment is not null
            ? $"\n\n[User attached: {attachment.FileName}]\n{attachment.Summary}"
            : string.Empty;

        messages.Add(new ChatMessageContent(
            AuthorRole.User,
            $"User question: {userQuestion}{attachmentNote}\n\nAgent responses:\n{context}"));

        return messages;
    }

    private static string BuildAttachmentContext(AttachedDocument? attachment)
    {
        if (attachment is null) return string.Empty;

        return $"""

            The user has attached a document to their message:
            Filename: {attachment.FileName}  |  Type: {attachment.ContentType}

            {attachment.Summary}

            Factor this attachment into your agent selection. If a "technical-drawing-extractor"
            or similar agent is available and the attached document appears to be an engineering
            document, prefer selecting it with high confidence.
            """;
    }
}
