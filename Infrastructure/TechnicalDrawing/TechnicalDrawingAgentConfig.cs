namespace BlazorAgentChat.Infrastructure.TechnicalDrawing;

/// <summary>
/// Configuration for a technical-drawing agent loaded from
/// Data/TechnicalDrawingAgents/agents.json.
/// </summary>
public sealed record TechnicalDrawingAgentConfig(
    string Id,
    string Name,
    string Description,
    bool   RequiresAttachment = true   // hint: this agent needs an attached document
);
