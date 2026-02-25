using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

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

        The user will ask a question. Your ONLY job is to decide which agents
        can help answer it. Respond with ONLY a valid JSON array of agent IDs
        (the "id" field), nothing else. Example: ["tax-guide","security-policy"]
        If no agent is relevant, respond with an empty array: []
        """;

    private const string SynthesisSystemPrompt = """
        You are a helpful assistant synthesizing information from multiple expert agents.
        Below are responses from agents who reviewed relevant documents or data.
        Provide a clear, accurate, and concise answer to the user's question
        based solely on these responses. If the agents did not provide enough
        information, say so clearly.
        """;

    public SkAgentRouter(KernelFactory kernelFactory, ILogger<SkAgentRouter> log)
    {
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    public async Task<IReadOnlyList<string>> SelectAgentsAsync(
        string                   userQuestion,
        IReadOnlyList<AgentInfo> available,
        CancellationToken        ct = default)
    {
        _log.LogDebug(
            "Routing question (length={Len}) across {Count} agents.",
            userQuestion.Length, available.Count);

        var agentList = string.Join("\n", available.Select(a =>
            $"  id: \"{a.Id}\"  name: \"{a.Name}\"  description: \"{a.Description}\""));

        var systemPrompt = RoutingSystemPrompt.Replace("{AGENT_LIST}", agentList);

        _log.LogTrace("Routing system prompt:\n{Prompt}", systemPrompt);

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userQuestion);

        var sw     = Stopwatch.StartNew();
        var result = await chatService.GetChatMessageContentAsync(history, cancellationToken: ct);
        var raw    = result.Content?.Trim() ?? "[]";
        sw.Stop();

        _log.LogDebug(
            "Routing LLM response in {Ms}ms: {Raw}",
            sw.ElapsedMilliseconds, raw);

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(raw) ?? [];
            _log.LogInformation(
                "Router selected {Count} agent(s): {Ids}",
                ids.Count, string.Join(", ", ids));
            return ids;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex,
                "Router returned non-JSON output. Raw='{Raw}'. Falling back to no agents.", raw);
            return [];
        }
    }

    public async IAsyncEnumerable<string> SynthesizeAsync(
        string                       userQuestion,
        IReadOnlyList<AgentResponse> agentResponses,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _log.LogDebug(
            "Synthesizing response from {Count} agent response(s).", agentResponses.Count);

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(SynthesisSystemPrompt);

        var context = string.Join("\n\n", agentResponses.Select(r =>
            $"--- {r.Agent.Name} ---\n{r.Content}"));

        _log.LogTrace("Synthesis context:\n{Context}", context);

        history.AddUserMessage($"User question: {userQuestion}\n\nAgent responses:\n{context}");

        int chunkCount = 0;
        var sw         = Stopwatch.StartNew();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                           history, cancellationToken: ct))
        {
            var text = chunk.Content;
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
}
