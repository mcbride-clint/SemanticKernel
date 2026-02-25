using System.Diagnostics;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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
        CancellationToken ct = default)
    {
        _log.LogDebug(
            "Invoking agent '{Name}' (id={Id}). Question length={Len}, PDF chars={Chars}.",
            agent.Name, agent.Id, question.Length, agent.PdfCharCount);

        var systemPrompt = AgentSystemPromptTemplate
            .Replace("{NAME}",        agent.Name)
            .Replace("{DESCRIPTION}", agent.Description)
            .Replace("{PDF_TEXT}",    agent.PdfText);

        _log.LogTrace(
            "Agent '{Name}' system prompt length={Len}", agent.Name, systemPrompt.Length);

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(question);

        // Auto function calling lets the LLM invoke registered KernelFunctions (e.g. DateTimePlugin)
        var settings = new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };

        var sw     = Stopwatch.StartNew();
        var result = await chatService.GetChatMessageContentAsync(history, settings, kernel, ct);
        sw.Stop();

        var content = result.Content ?? string.Empty;

        _log.LogDebug(
            "Agent '{Name}' responded in {Ms}ms. Response length={Len}.",
            agent.Name, sw.ElapsedMilliseconds, content.Length);

        _log.LogTrace("Agent '{Name}' full response:\n{Response}", agent.Name, content);

        return new AgentResponse(
            Agent:           agent,
            Content:         content,
            EstimatedTokens: content.Length / 4,
            Elapsed:         sw.Elapsed);
    }
}
