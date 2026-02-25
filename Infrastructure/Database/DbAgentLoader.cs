using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Loads DB-backed agents from Data/DatabaseAgents/agents.json.
/// Implements IAgentSource so the registry picks it up automatically alongside AgentLoader.
/// Also exposes GetConfig() so DbAgentRunner can retrieve connection details at call time.
/// </summary>
public sealed class DbAgentLoader : IAgentSource
{
    private readonly string                                    _configPath;
    private readonly ILogger<DbAgentLoader>                    _log;
    private readonly Dictionary<string, DatabaseAgentConfig>   _configs = [];

    public DbAgentLoader(ILogger<DbAgentLoader> log)
    {
        _log        = log;
        _configPath = Path.GetFullPath("Data/DatabaseAgents/agents.json");
    }

    public List<AgentInfo> LoadAll()
    {
        var results = new List<AgentInfo>();

        if (!File.Exists(_configPath))
        {
            _log.LogInformation(
                "No database agents config found at {Path}. Skipping DB agents.", _configPath);
            return results;
        }

        _log.LogInformation("Loading database agents from {Path}", _configPath);

        try
        {
            var json    = File.ReadAllText(_configPath);
            var entries = JsonSerializer.Deserialize<List<DatabaseAgentConfig>>(json,
                              new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? [];

            foreach (var cfg in entries)
            {
                _configs[cfg.Id] = cfg;

                var agent = new AgentInfo(
                    Id:           cfg.Id,
                    Name:         cfg.Name,
                    Description:  cfg.Description,
                    PdfText:      $"[Live database query — context fetched at query time]\nQuery: {cfg.ContextQuery}",
                    PdfCharCount: cfg.ContextQuery.Length,
                    SourceType:   "database");

                results.Add(agent);

                _log.LogInformation(
                    "Loaded DB agent '{Name}' (id={Id}). ContextQuery length={Len}.",
                    cfg.Name, cfg.Id, cfg.ContextQuery.Length);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load database agents from {Path}.", _configPath);
        }

        _log.LogInformation("DB agent loading complete. {Count} agent(s) loaded.", results.Count);
        return results;
    }

    /// <summary>Returns the runtime config (connection string, query) for a given agent ID.</summary>
    public DatabaseAgentConfig? GetConfig(string agentId) =>
        _configs.GetValueOrDefault(agentId);
}
