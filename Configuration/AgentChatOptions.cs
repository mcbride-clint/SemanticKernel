namespace BlazorAgentChat.Configuration;

public sealed class AgentChatOptions
{
    public const string SectionName = "AgentChat";

    public string AgentsDirectory { get; init; } = "Data/Agents";

    /// <summary>
    /// Minimum confidence score (0–1) an agent must reach during routing to be executed.
    /// Agents below this threshold are skipped. Default: 0.4.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.4;
}
