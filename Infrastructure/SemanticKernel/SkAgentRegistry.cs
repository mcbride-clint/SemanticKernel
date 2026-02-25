using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Services;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

public sealed class SkAgentRegistry : IAgentRegistry
{
    private readonly IReadOnlyList<AgentInfo>  _agents;
    private readonly ILogger<SkAgentRegistry>  _log;

    public SkAgentRegistry(AgentLoader loader, ILogger<SkAgentRegistry> log)
    {
        _log    = log;
        _agents = loader.LoadAll();

        _log.LogInformation(
            "Agent registry initialized with {Count} agent(s): {Names}",
            _agents.Count,
            string.Join(", ", _agents.Select(a => a.Name)));

        foreach (var a in _agents)
            _log.LogDebug(
                "  Agent '{Name}' (id={Id}, source={Source}): {CharCount} chars. Description: {Description}",
                a.Name, a.Id, a.SourceType, a.PdfCharCount, a.Description);
    }

    public IReadOnlyList<AgentInfo> GetAll() => _agents;

    public AgentInfo? FindById(string id) =>
        _agents.FirstOrDefault(a => a.Id == id);
}
