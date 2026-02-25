namespace BlazorAgentChat.Configuration;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string Endpoint   { get; init; } = string.Empty;
    public string ApiKey     { get; init; } = string.Empty;
    public string ModelId    { get; init; } = "gpt-4o";
    public string CaCertPath { get; init; } = string.Empty;
}
