using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.Rest;

/// <summary>
/// Loads REST-backed agents from Data/RestAgents/agents.json.
/// Implements IAgentSource so it is merged into the registry automatically.
/// Also exposes GetConfig() so RestAgentRunner can retrieve URL/auth details at call time.
/// </summary>
public sealed class RestAgentLoader : IAgentSource
{
    private readonly string                              _configPath;
    private readonly ILogger<RestAgentLoader>            _log;
    private readonly Dictionary<string, RestAgentConfig> _configs = [];

    public RestAgentLoader(ILogger<RestAgentLoader> log)
    {
        _log        = log;
        _configPath = Path.GetFullPath("Data/RestAgents/agents.json");
    }

    public List<AgentInfo> LoadAll()
    {
        var results = new List<AgentInfo>();

        if (!File.Exists(_configPath))
        {
            _log.LogInformation(
                "No REST agents config found at {Path}. Skipping REST agents.", _configPath);
            return results;
        }

        _log.LogInformation("Loading REST agents from {Path}", _configPath);

        try
        {
            var json    = File.ReadAllText(_configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var entries = JsonSerializer.Deserialize<List<RestAgentConfig>>(json, options) ?? [];

            foreach (var cfg in entries)
            {
                // Validate required fields before registering the agent.
                if (string.IsNullOrWhiteSpace(cfg.Id))
                {
                    _log.LogError("REST agent config is missing 'Id'. Skipping entry.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(cfg.UrlTemplate))
                {
                    _log.LogError("REST agent '{Id}' has empty UrlTemplate. Skipping.", cfg.Id);
                    continue;
                }
                if (!Uri.TryCreate(cfg.UrlTemplate.Split('{')[0], UriKind.Absolute, out _))
                {
                    _log.LogError(
                        "REST agent '{Id}' has invalid UrlTemplate '{Url}'. Skipping.",
                        cfg.Id, cfg.UrlTemplate);
                    continue;
                }
                if (cfg.TimeoutSeconds <= 0)
                {
                    _log.LogError(
                        "REST agent '{Id}' has invalid TimeoutSeconds={T}. Skipping.",
                        cfg.Id, cfg.TimeoutSeconds);
                    continue;
                }
                // Verify all required path parameters have matching {Name} tokens in the URL.
                var missingPathParams = (cfg.Parameters ?? [])
                    .Where(p => p.Required && p.Location == "path" &&
                                !cfg.UrlTemplate.Contains($"{{{p.Name}}}"))
                    .Select(p => p.Name)
                    .ToList();
                if (missingPathParams.Count > 0)
                {
                    _log.LogError(
                        "REST agent '{Id}' has required path parameter(s) {Params} not found in UrlTemplate. Skipping.",
                        cfg.Id, string.Join(", ", missingPathParams));
                    continue;
                }

                _configs[cfg.Id] = cfg;

                var agent = new AgentInfo(
                    Id:           cfg.Id,
                    Name:         cfg.Name,
                    Description:  cfg.Description,
                    PdfText:      $"[Live REST API — data fetched at query time]\nEndpoint: {cfg.UrlTemplate}",
                    PdfCharCount: cfg.UrlTemplate.Length,
                    SourceType:   "rest");

                results.Add(agent);

                _log.LogInformation(
                    "Loaded REST agent '{Name}' (id={Id}). Endpoint: {Url}",
                    cfg.Name, cfg.Id, cfg.UrlTemplate);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load REST agents from {Path}.", _configPath);
        }

        _log.LogInformation("REST agent loading complete. {Count} agent(s) loaded.", results.Count);
        return results;
    }

    public RestAgentConfig? GetConfig(string agentId) =>
        _configs.GetValueOrDefault(agentId);
}
