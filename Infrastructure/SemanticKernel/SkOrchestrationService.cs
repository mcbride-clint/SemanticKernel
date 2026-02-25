using System.Diagnostics;
using System.Runtime.CompilerServices;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class SkOrchestrationService : IOrchestrationService
{
    private readonly IAgentRegistry                  _registry;
    private readonly IAgentRouter                    _router;
    private readonly IAgentRunner                    _runner;
    private readonly ILogger<SkOrchestrationService> _log;

    public OrchestrationMetadata? LastMetadata { get; private set; }

    public SkOrchestrationService(
        IAgentRegistry                   registry,
        IAgentRouter                     router,
        IAgentRunner                     runner,
        ILogger<SkOrchestrationService>  log)
    {
        _registry = registry;
        _router   = router;
        _runner   = runner;
        _log      = log;
    }

    public async IAsyncEnumerable<string> AskAsync(
        string userQuestion,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var totalSw       = Stopwatch.StartNew();

        _log.LogInformation(
            "[{CorrId}] New question received. Length={Len}",
            correlationId, userQuestion.Length);
        _log.LogTrace("[{CorrId}] Question text: {Question}", correlationId, userQuestion);

        // ── Step 1: Route ────────────────────────────────────────────────────────
        var allAgents   = _registry.GetAll();
        var selectedIds = await _router.SelectAgentsAsync(userQuestion, allAgents, ct);

        if (selectedIds.Count == 0)
        {
            _log.LogWarning(
                "[{CorrId}] No agents selected. Returning fallback.", correlationId);
            yield return "I could not find any relevant documents to answer your question.";
            LastMetadata = new OrchestrationMetadata(correlationId, [], totalSw.Elapsed);
            yield break;
        }

        var selectedAgents = selectedIds
            .Select(id => _registry.FindById(id))
            .Where(a => a is not null)
            .Cast<AgentInfo>()
            .ToList();

        _log.LogInformation(
            "[{CorrId}] Routing to {Count} agent(s): {Names}",
            correlationId, selectedAgents.Count,
            string.Join(", ", selectedAgents.Select(a => a.Name)));

        // ── Step 2: Run agents in parallel ───────────────────────────────────────
        var agentTasks = selectedAgents
            .Select(agent => _runner.RunAsync(agent, userQuestion, ct))
            .ToList();

        AgentResponse[]? agentResponses  = null;
        Exception?       agentException = null;

        try
        {
            agentResponses = await Task.WhenAll(agentTasks);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "[{CorrId}] One or more agent invocations failed.", correlationId);
            agentException = ex;
        }

        if (agentException is not null)
        {
            yield return "An error occurred while consulting the expert agents.";
            yield break;
        }

        _log.LogDebug(
            "[{CorrId}] All {Count} agent(s) responded. Proceeding to synthesis.",
            correlationId, agentResponses!.Length);

        // ── Step 3: Synthesize ───────────────────────────────────────────────────
        await foreach (var token in _router.SynthesizeAsync(userQuestion, agentResponses, ct))
            yield return token;

        totalSw.Stop();
        LastMetadata = new OrchestrationMetadata(
            correlationId,
            selectedIds,
            totalSw.Elapsed);

        _log.LogInformation(
            "[{CorrId}] Orchestration complete. TotalElapsed={Ms}ms",
            correlationId, totalSw.ElapsedMilliseconds);
    }
}
