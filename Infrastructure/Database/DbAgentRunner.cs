using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Runs a predefined context query against a database, formats the results,
/// then sends them to the LLM to answer the user's question.
///
/// If the agent config defines Parameters, a lightweight LLM call first extracts
/// values from the user's question. Those values are injected as DbParameter objects
/// (never string-interpolated into SQL — no injection risk).
/// </summary>
public sealed class DbAgentRunner : IAgentRunner
{
    private readonly DbAgentLoader                    _loader;
    private readonly IDbConnectionFactory             _connectionFactory;
    private readonly SemanticKernel.KernelFactory     _kernelFactory;
    private readonly ILogger<DbAgentRunner>           _log;

    private const string AnswerSystemPromptTemplate = """
        You are an expert assistant for the database: "{NAME}"
        {DESCRIPTION}

        The following data was retrieved from the database to help you answer the question.
        Answer using ONLY this data. If the answer is not present, say:
        "I could not find information about that in the database."

        === DATABASE RESULTS START ===
        {DB_DATA}
        === DATABASE RESULTS END ===
        """;

    private const string ParameterExtractionSystemPrompt = """
        Extract database query parameters from the user's question.
        Return ONLY a valid JSON object mapping each parameter name to its extracted string value.
        Use JSON null for any parameter that the question does not specify.
        Do not include any explanation — output only the JSON object.

        Parameters to extract:
        {PARAMETER_DESCRIPTIONS}

        Examples:
          Question: "Who works in Engineering?"      → {{"Department": "Engineering"}}
          Question: "List all employees"             → {{"Department": null}}
          Question: "Show me products under $50"     → {{"MaxPrice": "50", "Category": null}}
        """;

    public DbAgentRunner(
        DbAgentLoader                    loader,
        IDbConnectionFactory             connectionFactory,
        SemanticKernel.KernelFactory     kernelFactory,
        ILogger<DbAgentRunner>           log)
    {
        _loader            = loader;
        _connectionFactory = connectionFactory;
        _kernelFactory     = kernelFactory;
        _log               = log;
    }

    public async Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        CancellationToken ct = default)
    {
        _log.LogDebug(
            "Invoking DB agent '{Name}' (id={Id}). Question length={Len}.",
            agent.Name, agent.Id, question.Length);

        var cfg = _loader.GetConfig(agent.Id);
        if (cfg is null)
        {
            _log.LogError("No database config found for agent id={Id}.", agent.Id);
            return new AgentResponse(agent, "Database configuration not found.", 0, TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();

        // ── Step 1 (optional): Extract parameters from question via LLM ──────────
        Dictionary<string, string?> extractedParams = [];

        if (cfg.Parameters is { Count: > 0 })
        {
            extractedParams = await ExtractParametersAsync(cfg, question, ct);

            // Fail fast for missing required parameters
            var missing = cfg.Parameters
                .Where(p => p.Required && string.IsNullOrWhiteSpace(extractedParams.GetValueOrDefault(p.Name)))
                .Select(p => p.Name)
                .ToList();

            if (missing.Count > 0)
            {
                var missingNames = string.Join(", ", missing);
                _log.LogWarning(
                    "DB agent '{Name}' missing required parameters: {Missing}", agent.Name, missingNames);
                return new AgentResponse(
                    agent,
                    $"I need more information to answer that question. Please specify: {missingNames}.",
                    0, sw.Elapsed);
            }
        }

        // ── Step 2: Fetch data from the database ──────────────────────────────────
        string dbData;

        try
        {
            dbData = await FetchDataAsync(cfg, extractedParams, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "DB query failed for agent '{Name}' (id={Id}).", agent.Name, agent.Id);
            return new AgentResponse(
                agent,
                "I was unable to retrieve data from the database at this time.",
                0, sw.Elapsed);
        }

        _log.LogDebug(
            "DB agent '{Name}' fetched {Len} chars of data in {Ms}ms.",
            agent.Name, dbData.Length, sw.ElapsedMilliseconds);
        _log.LogTrace("DB agent '{Name}' data:\n{Data}", agent.Name, dbData);

        // ── Step 3: Ask LLM to interpret the data ────────────────────────────────
        var systemPrompt = AnswerSystemPromptTemplate
            .Replace("{NAME}",        agent.Name)
            .Replace("{DESCRIPTION}", agent.Description)
            .Replace("{DB_DATA}",     dbData);

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(question);

        var result  = await chatService.GetChatMessageContentAsync(history, cancellationToken: ct);
        var content = result.Content ?? string.Empty;

        sw.Stop();
        _log.LogDebug(
            "DB agent '{Name}' responded in {Ms}ms total. Response length={Len}.",
            agent.Name, sw.ElapsedMilliseconds, content.Length);

        return new AgentResponse(
            Agent:           agent,
            Content:         content,
            EstimatedTokens: (dbData.Length + content.Length) / 4,
            Elapsed:         sw.Elapsed);
    }

    /// <summary>
    /// Makes a lightweight LLM call to extract parameter values from the user's question.
    /// Returns a dictionary of parameter name → extracted string value (null if not found).
    /// </summary>
    private async Task<Dictionary<string, string?>> ExtractParametersAsync(
        DatabaseAgentConfig cfg,
        string              question,
        CancellationToken   ct)
    {
        var paramDescriptions = string.Join("\n", cfg.Parameters!.Select(p =>
            $"- {p.Name} ({(p.Required ? "required" : "optional")}): {p.Description}"));

        var systemPrompt = ParameterExtractionSystemPrompt
            .Replace("{PARAMETER_DESCRIPTIONS}", paramDescriptions);

        _log.LogTrace(
            "Extracting parameters for DB agent id={Id}. Params: {Params}",
            cfg.Id, string.Join(", ", cfg.Parameters!.Select(p => p.Name)));

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(question);

        var sw     = Stopwatch.StartNew();
        var result = await chatService.GetChatMessageContentAsync(history, cancellationToken: ct);
        var raw    = result.Content?.Trim() ?? "{}";
        sw.Stop();

        _log.LogDebug(
            "Parameter extraction for id={Id} completed in {Ms}ms. Raw: {Raw}",
            cfg.Id, sw.ElapsedMilliseconds, raw);

        try
        {
            var doc    = JsonDocument.Parse(raw);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in cfg.Parameters!)
            {
                if (doc.RootElement.TryGetProperty(p.Name, out var elem))
                {
                    values[p.Name] = elem.ValueKind == JsonValueKind.Null
                        ? null
                        : elem.GetString();
                }
                else
                {
                    values[p.Name] = null;
                }
            }

            _log.LogInformation(
                "DB agent id={Id} extracted parameters: {Params}",
                cfg.Id,
                string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value ?? "null"}")));

            return values;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex,
                "Parameter extraction returned non-JSON for id={Id}. Raw='{Raw}'. Using nulls.", cfg.Id, raw);
            return cfg.Parameters!.ToDictionary(p => p.Name, _ => (string?)null);
        }
    }

    private async Task<string> FetchDataAsync(
        DatabaseAgentConfig          cfg,
        Dictionary<string, string?>  parameters,
        CancellationToken            ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cfg.ConnectionString, ct);
        await using var command    = connection.CreateCommand();
        command.CommandText = cfg.ContextQuery;

        // Add extracted parameters as safe DbParameter objects — never interpolated into SQL
        foreach (var (name, value) in parameters)
        {
            var param       = command.CreateParameter();
            param.ParameterName = name;
            param.Value         = value is null ? DBNull.Value : value;
            command.Parameters.Add(param);

            _log.LogDebug(
                "DB param @{Name} = {Value}", name, value ?? "NULL");
        }

        await using var reader = await command.ExecuteReaderAsync(ct);

        var sb          = new StringBuilder();
        var columnCount = reader.FieldCount;
        var rowCount    = 0;

        // Header row
        var headers = Enumerable.Range(0, columnCount).Select(reader.GetName);
        sb.AppendLine(string.Join(" | ", headers));
        sb.AppendLine(string.Join("-+-", Enumerable.Repeat("---", columnCount)));

        // Data rows
        while (await reader.ReadAsync(ct) && rowCount < cfg.MaxRows)
        {
            var values = Enumerable.Range(0, columnCount)
                .Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i).ToString() ?? "");
            sb.AppendLine(string.Join(" | ", values));
            rowCount++;
        }

        if (rowCount == cfg.MaxRows)
        {
            sb.AppendLine($"[Results truncated at {cfg.MaxRows} rows]");
            _log.LogWarning(
                "DB agent result truncated at {MaxRows} rows for agent id={Id}.",
                cfg.MaxRows, cfg.Id);
        }

        _log.LogInformation(
            "DB agent id={Id} fetched {Rows} rows, {Cols} columns.", cfg.Id, rowCount, columnCount);

        return sb.ToString();
    }
}
