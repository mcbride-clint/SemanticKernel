using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRouter
{
    /// <summary>
    /// Asks the LLM which agents from <paramref name="available"/> are relevant
    /// for <paramref name="userQuestion"/>. Returns agent IDs.
    /// </summary>
    Task<IReadOnlyList<string>> SelectAgentsAsync(
        string                   userQuestion,
        IReadOnlyList<AgentInfo> available,
        CancellationToken        ct = default);

    /// <summary>
    /// Given the original question and one or more agent responses, synthesizes
    /// a final answer. Returns tokens via IAsyncEnumerable for streaming.
    /// </summary>
    IAsyncEnumerable<string> SynthesizeAsync(
        string                       userQuestion,
        IReadOnlyList<AgentResponse> agentResponses,
        CancellationToken            ct = default);
}
