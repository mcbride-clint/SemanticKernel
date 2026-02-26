using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class SkOrchestrationService : IOrchestrationService
{
    private readonly IAgentRegistry                  _registry;
    private readonly IAgentRouter                    _router;
    private readonly IAgentRunner                    _runner;
    private readonly AgentChatActivitySource         _activitySource;
    private readonly ILogger<SkOrchestrationService> _log;

    // Scoped per Blazor circuit — stores recent turns for multi-turn context.
    private readonly List<ConversationTurn> _conversationHistory = [];
    private const int MaxHistoryTurns = 10;   // 5 exchanges

    public OrchestrationMetadata? LastMetadata { get; private set; }

    public SkOrchestrationService(
        IAgentRegistry                   registry,
        IAgentRouter                     router,
        IAgentRunner                     runner,
        AgentChatActivitySource          activitySource,
        ILogger<SkOrchestrationService>  log)
    {
        _registry       = registry;
        _router         = router;
        _runner         = runner;
        _activitySource = activitySource;
        _log            = log;
    }

    public async IAsyncEnumerable<string> AskAsync(
        string                              userQuestion,
        [EnumeratorCancellation] CancellationToken ct              = default,
        IReadOnlySet<string>?               enabledAgentIds = null,
        AttachedDocument?                   attachment      = null)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var totalSw       = Stopwatch.StartNew();

        using var orchestrationActivity = _activitySource.StartOrchestration(correlationId);

        _log.LogInformation(
            "[{CorrId}] New question. Length={Len}, Filter={Filter}, HasAttachment={Has}",
            correlationId, userQuestion.Length,
            enabledAgentIds is null ? "all" : string.Join(",", enabledAgentIds),
            attachment is not null);

        if (attachment is not null)
            _log.LogInformation(
                "[{CorrId}] Attachment: '{File}' ({Type}, {Bytes:N0} bytes).",
                correlationId, attachment.FileName, attachment.ContentType, attachment.SizeBytes);

        // ── Step 1: Route ────────────────────────────────────────────────────────
        var allAgents = _registry.GetAll();
        var availableAgents = enabledAgentIds is null
            ? allAgents
            : allAgents.Where(a => enabledAgentIds.Contains(a.Id)).ToList();

        using var routingActivity = _activitySource.StartRouting(availableAgents.Count);

        IReadOnlyList<AgentSelection> selections;
        try
        {
            selections = await _router.SelectAgentsAsync(
                userQuestion, availableAgents, attachment, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "[{CorrId}] Routing failed.", correlationId);
            yield return "An error occurred while routing your question to the right agents.";
            LastMetadata = new OrchestrationMetadata(correlationId, [], totalSw.Elapsed, []);
            yield break;
        }

        routingActivity?.SetTag("selected_agents", selections.Count);

        if (selections.Count == 0)
        {
            _log.LogWarning("[{CorrId}] No agents selected. Returning fallback.", correlationId);
            yield return "I could not find any relevant documents or data sources to answer your question.";
            LastMetadata = new OrchestrationMetadata(correlationId, [], totalSw.Elapsed, []);
            yield break;
        }

        var selectedAgents = selections
            .Select(s => (Selection: s, Agent: _registry.FindById(s.AgentId)))
            .Where(x => x.Agent is not null)
            .Select(x => (x.Selection, Agent: x.Agent!))
            .ToList();

        _log.LogInformation(
            "[{CorrId}] Routing to {Count} agent(s): {Names}",
            correlationId, selectedAgents.Count,
            string.Join(", ", selectedAgents.Select(x => $"{x.Agent.Name}({x.Selection.Confidence:P0})")));

        // ── Step 2: Run agents in parallel with per-agent error isolation ────────
        var agentTasks = selectedAgents
            .Select(x => RunAgentSafeAsync(x.Agent, x.Selection, userQuestion, attachment, correlationId, ct))
            .ToList();

        var results = await Task.WhenAll(agentTasks);

        var successfulResponses = results
            .Where(r => r.Response is not null)
            .Select(r => r.Response!)
            .ToList();

        var agentRunResults = results.Select(r => r.RunResult).ToList();

        var failedCount = results.Count(r => r.Response is null);
        if (failedCount > 0)
            _log.LogWarning(
                "[{CorrId}] {Failed}/{Total} agent(s) failed. Proceeding with partial results.",
                correlationId, failedCount, results.Length);

        if (successfulResponses.Count == 0)
        {
            yield return "All agent invocations failed. Please try again.";
            LastMetadata = new OrchestrationMetadata(
                correlationId, selections, totalSw.Elapsed, agentRunResults);
            yield break;
        }

        _log.LogDebug(
            "[{CorrId}] {Count} agent(s) responded. Proceeding to synthesis.",
            correlationId, successfulResponses.Count);

        // ── Step 3: Synthesize with streaming ────────────────────────────────────
        using var synthesisActivity = _activitySource.StartSynthesis(successfulResponses.Count);

        var fullResponse = new StringBuilder();
        await foreach (var token in _router.SynthesizeAsync(
                           userQuestion, successfulResponses, _conversationHistory, attachment, ct))
        {
            fullResponse.Append(token);
            yield return token;
        }

        synthesisActivity?.SetTag("response_length", fullResponse.Length);

        // Record exchange in conversation history for next turn
        _conversationHistory.Add(new ConversationTurn("user",      userQuestion));
        _conversationHistory.Add(new ConversationTurn("assistant", fullResponse.ToString()));
        while (_conversationHistory.Count > MaxHistoryTurns)
            _conversationHistory.RemoveAt(0);

        totalSw.Stop();
        LastMetadata = new OrchestrationMetadata(
            correlationId, selections, totalSw.Elapsed, agentRunResults);

        orchestrationActivity?.SetTag("total_elapsed_ms", totalSw.ElapsedMilliseconds);

        _log.LogInformation(
            "[{CorrId}] Orchestration complete. TotalElapsed={Ms}ms, HistoryTurns={Turns}",
            correlationId, totalSw.ElapsedMilliseconds, _conversationHistory.Count);
    }

    private async Task<(AgentRunResult RunResult, AgentResponse? Response)> RunAgentSafeAsync(
        AgentInfo         agent,
        AgentSelection    selection,
        string            question,
        AttachedDocument? attachment,
        string            correlationId,
        CancellationToken ct)
    {
        using var activity = _activitySource.StartAgentRun(agent.Id, agent.Name, agent.SourceType);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _runner.RunAsync(agent, question, attachment, ct);
            sw.Stop();
            activity?.SetTag("success", true)
                     .SetTag("elapsed_ms", sw.ElapsedMilliseconds)
                     .SetTag("estimated_tokens", response.EstimatedTokens);

            _log.LogDebug(
                "[{CorrId}] Agent '{Name}' succeeded in {Ms}ms.",
                correlationId, agent.Name, sw.ElapsedMilliseconds);

            return (
                new AgentRunResult(agent.Id, agent.Name, true, null, sw.Elapsed, response.EstimatedTokens),
                response
            );
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetTag("success", false).SetTag("error", ex.Message);
            _log.LogError(ex,
                "[{CorrId}] Agent '{Name}' (id={Id}) failed after {Ms}ms.",
                correlationId, agent.Name, agent.Id, sw.ElapsedMilliseconds);

            return (
                new AgentRunResult(agent.Id, agent.Name, false, ex.Message, sw.Elapsed, 0),
                null
            );
        }
    }
}
