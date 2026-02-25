using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRunner
{
    /// <summary>
    /// Invokes a single agent with a question.
    /// Returns the full response (individual agent calls are not streamed).
    /// </summary>
    Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        CancellationToken ct = default);
}
