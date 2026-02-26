using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRouter
{
    /// <summary>
    /// Asks the LLM which agents are relevant for the question.
    /// When an attachment is supplied its summary is included in the routing context
    /// so the router can select attachment-aware agents (e.g. technical-drawing-extractor).
    /// Returns weighted selections with confidence scores (0.0–1.0) and optional rationale.
    /// </summary>
    Task<IReadOnlyList<AgentSelection>> SelectAgentsAsync(
        string                   userQuestion,
        IReadOnlyList<AgentInfo> available,
        AttachedDocument?        attachment = null,
        CancellationToken        ct         = default);

    /// <summary>
    /// Synthesizes a final answer from agent responses.
    /// Includes prior conversation history and, when present, attachment context.
    /// Returns tokens via IAsyncEnumerable for streaming.
    /// </summary>
    IAsyncEnumerable<string> SynthesizeAsync(
        string                           userQuestion,
        IReadOnlyList<AgentResponse>     agentResponses,
        IReadOnlyList<ConversationTurn>? history    = null,
        AttachedDocument?                attachment = null,
        CancellationToken                ct         = default);
}
