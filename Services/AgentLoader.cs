using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorAgentChat.Services;

public sealed class AgentLoader : IAgentSource
{
    private readonly AgentChatOptions      _opts;
    private readonly PdfTextExtractor      _extractor;
    private readonly ILogger<AgentLoader>  _log;

    public AgentLoader(
        IOptions<AgentChatOptions> opts,
        PdfTextExtractor           extractor,
        ILogger<AgentLoader>       log)
    {
        _opts      = opts.Value;
        _extractor = extractor;
        _log       = log;
    }

    public List<AgentInfo> LoadAll()
    {
        _log.LogInformation("Scanning agents directory: {Dir}", _opts.AgentsDirectory);

        var results = new List<AgentInfo>();
        var baseDir = Path.GetFullPath(_opts.AgentsDirectory);

        if (!Directory.Exists(baseDir))
        {
            _log.LogError("Agents directory not found: {Dir}", baseDir);
            return results;
        }

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            var jsonPath = Path.Combine(dir, "agent.json");
            var pdfPath  = Path.Combine(dir, "document.pdf");

            if (!File.Exists(jsonPath) || !File.Exists(pdfPath))
            {
                _log.LogWarning(
                    "Skipping '{Dir}': missing agent.json or document.pdf",
                    Path.GetFileName(dir));
                continue;
            }

            try
            {
                var json    = File.ReadAllText(jsonPath);
                var meta    = JsonSerializer.Deserialize<AgentJsonMeta>(json,
                                  new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                var pdfText = _extractor.Extract(pdfPath);
                var id      = Path.GetFileName(dir).ToLowerInvariant().Replace(" ", "-");

                var agent = new AgentInfo(
                    Id:           id,
                    Name:         meta.Name,
                    Description:  meta.Description,
                    PdfText:      pdfText,
                    PdfCharCount: pdfText.Length,
                    SourceType:   "pdf");

                results.Add(agent);
                _log.LogInformation(
                    "Loaded agent '{Name}' (id={Id}) from '{Dir}'.",
                    agent.Name, agent.Id, Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Failed to load agent from '{Dir}'. Skipping.", Path.GetFileName(dir));
            }
        }

        _log.LogInformation("Agent loading complete. {Count} agent(s) loaded.", results.Count);
        return results;
    }

    private sealed record AgentJsonMeta(string Name, string Description);
}
