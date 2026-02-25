namespace BlazorAgentChat.Abstractions.Models;

public sealed record AgentResponse(
    AgentInfo Agent,
    string    Content,
    int       EstimatedTokens,
    TimeSpan  Elapsed
);
