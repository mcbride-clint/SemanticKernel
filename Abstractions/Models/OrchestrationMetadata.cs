namespace BlazorAgentChat.Abstractions.Models;

public sealed record OrchestrationMetadata(
    string                CorrelationId,
    IReadOnlyList<string> SelectedAgentIds,
    TimeSpan              TotalElapsed
);
