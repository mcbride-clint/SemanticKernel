using System.Data.Common;
using System.Diagnostics;
using System.Text;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Runs a predefined context query against a database, formats the results,
/// then sends them to the LLM to answer the user's question.
///
/// If the agent config defines Parameters, ParameterExtractor makes a lightweight
/// LLM call to extract values from the question. Those values are injected as
/// DbParameter objects — never string-interpolated into SQL.
/// </summary>
public sealed class DbAgentRunner : IAgentRunner
{
    private readonly DbAgentLoader                _loader;
    private readonly IDbConnectionFactory         _connectionFactory;
    private readonly SemanticKernel.KernelFactory _kernelFactory;
    private readonly ParameterExtractor           _parameterExtractor;
    private readonly ILogger<DbAgentRunner>       _log;

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

    public DbAgentRunner(
        DbAgentLoader                    loader,
        IDbConnectionFactory             connectionFactory,
        SemanticKernel.KernelFactory     kernelFactory,
        ParameterExtractor               parameterExtractor,
        ILogger<DbAgentRunner>           log)
    {
        _loader             = loader;
        _connectionFactory  = connectionFactory;
        _kernelFactory      = kernelFactory;
        _parameterExtractor = parameterExtractor;
        _log                = log;
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
            var specs = cfg.Parameters.Select(p => (p.Name, p.Description, p.Required)).ToList();
            extractedParams = await _parameterExtractor.ExtractAsync(cfg.Id, specs, question, ct);

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

    private async Task<string> FetchDataAsync(
        DatabaseAgentConfig         cfg,
        Dictionary<string, string?> parameters,
        CancellationToken           ct)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cfg.ConnectionString, ct);
        await using var command    = connection.CreateCommand();
        command.CommandText = cfg.ContextQuery;

        // Inject as safe DbParameter objects — never interpolated into SQL
        foreach (var (name, value) in parameters)
        {
            var param           = command.CreateParameter();
            param.ParameterName = name;
            param.Value         = value is null ? DBNull.Value : value;
            command.Parameters.Add(param);
            _log.LogDebug("DB param @{Name} = {Value}", name, value ?? "NULL");
        }

        await using var reader = await command.ExecuteReaderAsync(ct);

        var sb          = new StringBuilder();
        var columnCount = reader.FieldCount;
        var rowCount    = 0;

        var headers = Enumerable.Range(0, columnCount).Select(reader.GetName);
        sb.AppendLine(string.Join(" | ", headers));
        sb.AppendLine(string.Join("-+-", Enumerable.Repeat("---", columnCount)));

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
