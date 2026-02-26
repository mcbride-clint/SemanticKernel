namespace BlazorAgentChat.Abstractions.Models;

/// <summary>
/// Represents an agent selected by the router, with the router's confidence
/// that the agent is relevant to the current question.
/// </summary>
public sealed record AgentSelection(
    string  AgentId,
    double  Confidence,      // 0.0 (not relevant) – 1.0 (highly relevant)
    string? Reason = null    // brief rationale returned by the router
);
