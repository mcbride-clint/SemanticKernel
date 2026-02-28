namespace BlazorAgentChat.Abstractions.Models;

public sealed record OrchestrationMetadata(
    string                        CorrelationId,
    IReadOnlyList<AgentSelection> SelectedAgents,
    TimeSpan                      TotalElapsed,
    IReadOnlyList<AgentRunResult> AgentResults,
    TimeSpan                      RoutingElapsed   = default,
    TimeSpan                      SynthesisElapsed = default
)
{
    /// <summary>Backward-compatible accessor for selected agent IDs.</summary>
    public IReadOnlyList<string> SelectedAgentIds =>
        SelectedAgents.Select(a => a.AgentId).ToList();
}
