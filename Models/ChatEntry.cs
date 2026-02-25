namespace BlazorAgentChat.Models;

public sealed class ChatEntry
{
    public enum ChatRole { User, Assistant, System }

    public ChatRole Role      { get; init; }
    public string   Content   { get; set; } = string.Empty;   // mutable for streaming append
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string?  AgentInfo { get; init; }   // e.g. "Answered by: Tax Guide, Security Policy"
}
