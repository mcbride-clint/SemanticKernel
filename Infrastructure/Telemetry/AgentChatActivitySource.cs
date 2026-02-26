using System.Diagnostics;

namespace BlazorAgentChat.Infrastructure.Telemetry;

/// <summary>
/// Central ActivitySource for the BlazorAgentChat pipeline.
/// Activities are automatically picked up by any registered OpenTelemetry
/// exporter when the source name is registered with AddSource("BlazorAgentChat").
/// </summary>
public sealed class AgentChatActivitySource : IDisposable
{
    public const string SourceName    = "BlazorAgentChat";
    public const string SourceVersion = "1.0.0";

    private readonly ActivitySource _source = new(SourceName, SourceVersion);

    public Activity? StartOrchestration(string correlationId) =>
        _source.StartActivity("orchestration")
               ?.SetTag("correlation_id", correlationId);

    public Activity? StartRouting(int agentCount) =>
        _source.StartActivity("routing")
               ?.SetTag("available_agents", agentCount);

    public Activity? StartAgentRun(string agentId, string agentName, string sourceType) =>
        _source.StartActivity("agent_run")
               ?.SetTag("agent.id",          agentId)
               ?.SetTag("agent.name",         agentName)
               ?.SetTag("agent.source_type",  sourceType);

    public Activity? StartSynthesis(int responseCount) =>
        _source.StartActivity("synthesis")
               ?.SetTag("agent_response_count", responseCount);

    public void Dispose() => _source.Dispose();
}
