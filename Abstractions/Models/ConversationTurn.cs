namespace BlazorAgentChat.Abstractions.Models;

/// <summary>
/// A single turn in the conversation history (user or assistant message).
/// Used to give the synthesis LLM multi-turn context.
/// </summary>
public sealed record ConversationTurn(
    string Role,     // "user" or "assistant"
    string Content
);
