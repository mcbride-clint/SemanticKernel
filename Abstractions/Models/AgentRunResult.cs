namespace BlazorAgentChat.Abstractions.Models;

/// <summary>
/// Per-agent execution result stored in OrchestrationMetadata.
/// Captures success/failure, timing, and token usage for the debug page.
/// </summary>
public sealed record AgentRunResult(
    string   AgentId,
    string   AgentName,
    bool     Success,
    string?  ErrorMessage,
    TimeSpan Elapsed,
    int      EstimatedTokens
);
