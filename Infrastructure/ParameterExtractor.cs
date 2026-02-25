using System.Diagnostics;
using System.Text.Json;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure;

/// <summary>
/// Shared service used by any runner (DB, REST, etc.) that needs to extract
/// named parameter values from a user's question via a lightweight LLM call.
/// Accepts plain tuples so it stays decoupled from any specific parameter config type.
/// </summary>
public sealed class ParameterExtractor
{
    private readonly KernelFactory              _kernelFactory;
    private readonly ILogger<ParameterExtractor> _log;

    private const string SystemPrompt = """
        Extract query parameters from the user's question.
        Return ONLY a valid JSON object mapping each parameter name to its extracted string value.
        Use JSON null for any parameter the question does not specify.
        Do not include explanation — output only the JSON object.

        Parameters to extract:
        {PARAMETER_DESCRIPTIONS}

        Examples:
          Question: "Who works in Engineering?"      → {{"Department": "Engineering"}}
          Question: "List all employees"             → {{"Department": null}}
          Question: "Show me products under $50"     → {{"MaxPrice": "50", "Category": null}}
          Question: "Get details for user 7"         → {{"UserId": "7"}}
        """;

    public ParameterExtractor(KernelFactory kernelFactory, ILogger<ParameterExtractor> log)
    {
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    /// <param name="agentId">Used only for log correlation.</param>
    /// <param name="parameters">Name, Description, Required tuples — provider-agnostic.</param>
    /// <param name="question">The raw user question.</param>
    public async Task<Dictionary<string, string?>> ExtractAsync(
        string agentId,
        IReadOnlyList<(string Name, string Description, bool Required)> parameters,
        string question,
        CancellationToken ct = default)
    {
        var paramDescriptions = string.Join("\n", parameters.Select(p =>
            $"- {p.Name} ({(p.Required ? "required" : "optional")}): {p.Description}"));

        var prompt = SystemPrompt.Replace("{PARAMETER_DESCRIPTIONS}", paramDescriptions);

        _log.LogTrace(
            "Extracting parameters for agent id={Id}. Params: {Params}",
            agentId, string.Join(", ", parameters.Select(p => p.Name)));

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(prompt);
        history.AddUserMessage(question);

        var sw     = Stopwatch.StartNew();
        var result = await chatService.GetChatMessageContentAsync(history, cancellationToken: ct);
        var raw    = result.Content?.Trim() ?? "{}";
        sw.Stop();

        _log.LogDebug(
            "Parameter extraction for agent id={Id} completed in {Ms}ms. Raw: {Raw}",
            agentId, sw.ElapsedMilliseconds, raw);

        try
        {
            var doc    = JsonDocument.Parse(raw);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, _, _) in parameters)
            {
                values[name] = doc.RootElement.TryGetProperty(name, out var elem) &&
                               elem.ValueKind != JsonValueKind.Null
                    ? elem.GetString()
                    : null;
            }

            _log.LogInformation(
                "Agent id={Id} extracted: {Params}",
                agentId,
                string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value ?? "null"}")));

            return values;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex,
                "Parameter extraction returned non-JSON for agent id={Id}. Raw='{Raw}'. Using nulls.",
                agentId, raw);
            return parameters.ToDictionary(p => p.Name, _ => (string?)null);
        }
    }
}
