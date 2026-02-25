namespace BlazorAgentChat.Configuration;

public sealed class AgentChatOptions
{
    public const string SectionName = "AgentChat";

    public string AgentsDirectory { get; init; } = "Data/Agents";
}
