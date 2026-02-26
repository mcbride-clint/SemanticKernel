using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.TechnicalDrawing;

/// <summary>
/// Loads technical-drawing agents from Data/TechnicalDrawingAgents/agents.json.
/// Follows the same pattern as DbAgentLoader — implements IAgentSource for the registry
/// and exposes GetConfig() so TechnicalDrawingAgentRunner can retrieve settings at runtime.
/// </summary>
public sealed class TechnicalDrawingAgentLoader : IAgentSource
{
    private readonly string                                              _configPath;
    private readonly Dictionary<string, TechnicalDrawingAgentConfig>    _configs = [];
    private readonly ILogger<TechnicalDrawingAgentLoader>               _log;

    public TechnicalDrawingAgentLoader(ILogger<TechnicalDrawingAgentLoader> log)
    {
        _log        = log;
        _configPath = Path.GetFullPath("Data/TechnicalDrawingAgents/agents.json");
    }

    public List<AgentInfo> LoadAll()
    {
        var results = new List<AgentInfo>();

        if (!File.Exists(_configPath))
        {
            _log.LogInformation(
                "No technical-drawing agents config at {Path}. Skipping.", _configPath);
            return results;
        }

        _log.LogInformation("Loading technical-drawing agents from {Path}", _configPath);

        try
        {
            var json    = File.ReadAllText(_configPath);
            var entries = JsonSerializer.Deserialize<List<TechnicalDrawingAgentConfig>>(json,
                              new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? [];

            foreach (var cfg in entries)
            {
                _configs[cfg.Id] = cfg;

                var agent = new AgentInfo(
                    Id:           cfg.Id,
                    Name:         cfg.Name,
                    Description:  cfg.Description,
                    PdfText:      string.Empty,   // no pre-loaded document; reads from attachment
                    PdfCharCount: 0,
                    SourceType:   "technical-drawing");

                results.Add(agent);

                _log.LogInformation(
                    "Loaded technical-drawing agent '{Name}' (id={Id}).", cfg.Name, cfg.Id);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load technical-drawing agents from {Path}.", _configPath);
        }

        return results;
    }

    public TechnicalDrawingAgentConfig? GetConfig(string agentId) =>
        _configs.GetValueOrDefault(agentId);
}
