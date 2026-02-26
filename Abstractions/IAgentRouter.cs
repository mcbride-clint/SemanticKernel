using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRouter
{
    /// <summary>
    /// Asks the LLM which agents from <paramref name="available"/> are relevant
    /// for <paramref name="userQuestion"/>. Returns weighted agent selections with
    /// confidence scores (0.0–1.0) and optional rationale.
    /// </summary>
    Task<IReadOnlyList<AgentSelection>> SelectAgentsAsync(
        string                   userQuestion,
        IReadOnlyList<AgentInfo> available,
        CancellationToken        ct = default);

    /// <summary>
    /// Given the original question and one or more agent responses, synthesizes
    /// a final answer. Optionally includes prior conversation history for multi-turn context.
    /// Returns tokens via IAsyncEnumerable for streaming.
    /// </summary>
    IAsyncEnumerable<string> SynthesizeAsync(
        string                           userQuestion,
        IReadOnlyList<AgentResponse>     agentResponses,
        IReadOnlyList<ConversationTurn>? history = null,
        CancellationToken                ct      = default);
}
