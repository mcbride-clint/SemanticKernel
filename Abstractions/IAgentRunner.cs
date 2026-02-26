using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRunner
{
    /// <summary>
    /// Invokes a single agent with a question.
    /// Returns the full response (individual agent calls are not streamed).
    /// </summary>
    /// <param name="attachment">
    /// Optional user-uploaded file. Agents may use this as additional context
    /// or (for technical-drawing agents) as the primary data source.
    /// </param>
    Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        AttachedDocument? attachment = null,
        CancellationToken ct         = default);
}
