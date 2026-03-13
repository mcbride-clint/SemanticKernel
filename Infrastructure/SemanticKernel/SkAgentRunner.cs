using System.Diagnostics;
using System.Text;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class SkAgentRunner : IAgentRunner
{
    private readonly KernelFactory          _kernelFactory;
    private readonly ILogger<SkAgentRunner> _log;

    private const string AgentSystemPromptTemplate = """
        You are an expert assistant for the document: "{NAME}"
        {DESCRIPTION}

        The complete document text is provided below. Answer questions using ONLY
        information found in this document. If the answer is not present, say:
        "I could not find information about that in this document."

        === DOCUMENT START ===
        {PDF_TEXT}
        === DOCUMENT END ===
        """;

    public SkAgentRunner(KernelFactory kernelFactory, ILogger<SkAgentRunner> log)
    {
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    public async Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        AttachedDocument? attachment = null,
        CancellationToken ct         = default)
    {
        _log.LogDebug(
            "Invoking agent '{Name}' (id={Id}). Question length={Len}, PDF chars={Chars}, HasAttachment={Has}.",
            agent.Name, agent.Id, question.Length, agent.PdfCharCount, attachment is not null);

        var systemPrompt = AgentSystemPromptTemplate
            .Replace("{NAME}",        agent.Name)
            .Replace("{DESCRIPTION}", agent.Description)
            .Replace("{PDF_TEXT}",    agent.PdfText);

        if (!string.IsNullOrWhiteSpace(agent.SystemPromptSuffix))
            systemPrompt += "\n\n" + agent.SystemPromptSuffix;

        // Build user message — include attachment as additional context if provided
        var userMessage = BuildUserMessage(question, attachment);

        var kernel      = _kernelFactory.Create();
        var chatAgent   = AgentKernelFactory.Create(agent.Name, systemPrompt, kernel, enableFunctions: true);

        var sw      = Stopwatch.StartNew();

        // Each retry gets a fresh ChatHistoryAgentThread so partial state never bleeds across attempts.
        var content = await RetryHelper.ExecuteAsync(async ck =>
        {
            var thread = new ChatHistoryAgentThread();
            var sb     = new StringBuilder();

            await foreach (var item in chatAgent.InvokeAsync(
                               [new ChatMessageContent(AuthorRole.User, userMessage)],
                               thread, options: null, ck))
            {
                sb.Append(item.Message.Content);
            }

            return sb.ToString();
        }, _log, $"pdf-agent:{agent.Id}", maxAttempts: 3, ct);

        sw.Stop();

        _log.LogDebug(
            "Agent '{Name}' responded in {Ms}ms. Response length={Len}.",
            agent.Name, sw.ElapsedMilliseconds, content.Length);

        return new AgentResponse(
            Agent:           agent,
            Content:         content,
            EstimatedTokens: content.Length / 4,
            Elapsed:         sw.Elapsed);
    }

    private static string BuildUserMessage(string question, AttachedDocument? attachment)
    {
        if (attachment is null) return question;

        var attachNote = attachment.HasText
            ? $"\n\n[User also attached: {attachment.FileName}]\n" +
              $"=== ATTACHMENT CONTENT ===\n{attachment.ExtractedText}\n=== END ATTACHMENT ==="
            : $"\n\n[User also attached: {attachment.FileName}]\n{attachment.Summary}";

        return question + attachNote;
    }
}
