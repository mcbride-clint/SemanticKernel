namespace BlazorAgentChat.Models;

public sealed class ChatEntry
{
    public enum ChatRole { User, Assistant, System }

    public Guid     Id        { get; }      = Guid.NewGuid();
    public ChatRole Role      { get; init; }
    public string   Content   { get; set; } = string.Empty;   // mutable for streaming append
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string?  AgentInfo { get; set; }   // e.g. "Consulted: Tax Guide (95%), Security Policy (60%)"
}
