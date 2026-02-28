using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Infrastructure;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace BlazorAgentChat.Infrastructure.Rest;

/// <summary>
/// Calls a REST API endpoint, feeds the response body to the LLM, and returns
/// a natural-language answer.
///
/// Pipeline per question:
///   1. Extract parameters from question via ParameterExtractor (LLM call)
///   2. Build request URL — path params replace {Name} in UrlTemplate,
///      query params are appended as ?key=value
///   3. Execute HTTP request with static headers + timeout
///   4. Ask LLM to interpret the response body
/// </summary>
public sealed class RestAgentRunner : IAgentRunner
{
    private readonly RestAgentLoader                  _loader;
    private readonly ParameterExtractor               _parameterExtractor;
    private readonly IHttpClientFactory               _httpClientFactory;
    private readonly SemanticKernel.KernelFactory     _kernelFactory;
    private readonly ILogger<RestAgentRunner>         _log;

    private const string AnswerSystemPromptTemplate = """
        You are an expert assistant connected to the API: "{NAME}"
        {DESCRIPTION}

        The following data was returned by the API. Answer the user's question using
        ONLY this data. If the answer is not present, say:
        "I could not find information about that from the API."

        === API RESPONSE START ===
        {API_DATA}
        === API RESPONSE END ===
        """;

    public RestAgentRunner(
        RestAgentLoader                  loader,
        ParameterExtractor               parameterExtractor,
        IHttpClientFactory               httpClientFactory,
        SemanticKernel.KernelFactory     kernelFactory,
        ILogger<RestAgentRunner>         log)
    {
        _loader             = loader;
        _parameterExtractor = parameterExtractor;
        _httpClientFactory  = httpClientFactory;
        _kernelFactory      = kernelFactory;
        _log                = log;
    }

    public async Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        AttachedDocument? attachment = null,
        CancellationToken ct = default)
    {
        _log.LogDebug(
            "Invoking REST agent '{Name}' (id={Id}). Question length={Len}.",
            agent.Name, agent.Id, question.Length);

        var cfg = _loader.GetConfig(agent.Id);
        if (cfg is null)
        {
            _log.LogError("No REST config found for agent id={Id}.", agent.Id);
            return new AgentResponse(agent, "REST agent configuration not found.", 0, TimeSpan.Zero);
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
                    "REST agent '{Name}' missing required parameters: {Missing}", agent.Name, missingNames);
                return new AgentResponse(
                    agent,
                    $"I need more information to answer that question. Please specify: {missingNames}.",
                    0, sw.Elapsed);
            }
        }

        // ── Step 2: Build the request URL and optional body ───────────────────────
        var url  = BuildUrl(cfg, extractedParams);
        var body = BuildRequestBody(cfg, extractedParams);
        _log.LogDebug("REST agent '{Name}' calling: {Method} {Url}", agent.Name, cfg.Method, url);

        // ── Step 3: Execute HTTP request ──────────────────────────────────────────
        string apiData;

        try
        {
            apiData = await FetchResponseAsync(cfg, url, body, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "REST request failed for agent '{Name}' (id={Id}).", agent.Name, agent.Id);
            return new AgentResponse(
                agent,
                "I was unable to reach the API at this time.",
                0, sw.Elapsed);
        }

        _log.LogDebug(
            "REST agent '{Name}' received {Len} chars in {Ms}ms.",
            agent.Name, apiData.Length, sw.ElapsedMilliseconds);
        _log.LogTrace("REST agent '{Name}' response:\n{Data}", agent.Name, apiData);

        // ── Step 4: Ask LLM to interpret the response ─────────────────────────────
        var systemPrompt = AnswerSystemPromptTemplate
            .Replace("{NAME}",        agent.Name)
            .Replace("{DESCRIPTION}", agent.Description)
            .Replace("{API_DATA}",    apiData);

        // Build user message — include attachment context when present
        var userMessage = question;
        if (attachment is not null)
        {
            var attachNote = attachment.HasText
                ? $"\n\n[User also attached: {attachment.FileName}]\n" +
                  $"=== ATTACHMENT CONTENT ===\n{attachment.ExtractedText}\n=== END ATTACHMENT ==="
                : $"\n\n[User also attached: {attachment.FileName}]\n{attachment.Summary}";
            userMessage = question + attachNote;
        }

        var kernel    = _kernelFactory.Create();
        var chatAgent = SemanticKernel.AgentKernelFactory.Create(
            agent.Name, systemPrompt, kernel, enableFunctions: false);

        // Each retry uses a fresh ChatHistoryAgentThread to avoid dirty state.
        var content = await RetryHelper.ExecuteAsync(async ck =>
        {
            var thread = new ChatHistoryAgentThread();
            var sb     = new StringBuilder();

            await foreach (var item in chatAgent.InvokeAsync(
                               [new ChatMessageContent(AuthorRole.User, userMessage)],
                               thread, options: null, ck))
            {
                sb.Append(item.Message.Content);
            }

            return sb.ToString();
        }, _log, $"rest-agent:{agent.Id}", maxAttempts: 3, ct);

        sw.Stop();
        _log.LogDebug(
            "REST agent '{Name}' responded in {Ms}ms total. Response length={Len}.",
            agent.Name, sw.ElapsedMilliseconds, content.Length);

        return new AgentResponse(
            Agent:           agent,
            Content:         content,
            EstimatedTokens: (apiData.Length + content.Length) / 4,
            Elapsed:         sw.Elapsed);
    }

    private static string BuildUrl(
        RestAgentConfig             cfg,
        Dictionary<string, string?> parameters)
    {
        // ── Path parameters: replace {ParamName} in the URL template ──────────────
        var url = cfg.UrlTemplate;
        foreach (var p in cfg.Parameters ?? [])
        {
            if (p.Location == "path")
            {
                var value = parameters.GetValueOrDefault(p.Name);
                url = url.Replace($"{{{p.Name}}}", HttpUtility.UrlEncode(value ?? string.Empty));
            }
        }

        // ── Query parameters ───────────────────────────────────────────────────────
        var query = HttpUtility.ParseQueryString(string.Empty);

        // Static query params first (e.g. api_version=2024-01-01)
        foreach (var (key, value) in cfg.StaticQueryParams ?? new Dictionary<string, string>())
            query[key] = value;

        // Extracted query params
        foreach (var p in cfg.Parameters ?? [])
        {
            if (p.Location == "query")
            {
                var value = parameters.GetValueOrDefault(p.Name);
                if (!string.IsNullOrWhiteSpace(value))
                    query[p.QueryKey ?? p.Name] = value;
            }
        }

        var queryString = query.ToString();
        return string.IsNullOrEmpty(queryString) ? url : $"{url}?{queryString}";
    }

    /// <summary>
    /// Builds the JSON request body from <see cref="RestAgentConfig.BodyTemplate"/>,
    /// replacing {ParamName} tokens with extracted values. Returns null when no template is set.
    /// </summary>
    private static string? BuildRequestBody(
        RestAgentConfig             cfg,
        Dictionary<string, string?> parameters)
    {
        if (string.IsNullOrWhiteSpace(cfg.BodyTemplate))
            return null;

        var body = cfg.BodyTemplate;
        foreach (var p in cfg.Parameters ?? [])
        {
            if (p.Location == "body")
            {
                var value = parameters.GetValueOrDefault(p.Name) ?? string.Empty;
                body = body.Replace($"{{{p.Name}}}", value);
            }
        }

        return body;
    }

    private async Task<string> FetchResponseAsync(
        RestAgentConfig   cfg,
        string            url,
        string?           requestBody,
        CancellationToken ct)
    {
        using var client  = _httpClientFactory.CreateClient();
        client.Timeout    = TimeSpan.FromSeconds(cfg.TimeoutSeconds);

        using var request = new HttpRequestMessage(new HttpMethod(cfg.Method), url);

        // Static headers (API keys, auth tokens, etc.)
        foreach (var (key, value) in cfg.StaticHeaders ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(key, value);

        // Attach request body when provided (POST/PUT with BodyTemplate)
        if (requestBody is not null)
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);

        _log.LogInformation(
            "REST agent id={Id} HTTP {Status} from {Url}",
            cfg.Id, (int)response.StatusCode, url);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning(
                "REST agent id={Id} non-success status {Status}. Body: {Body}",
                cfg.Id, (int)response.StatusCode, errorBody[..Math.Min(200, errorBody.Length)]);
            throw new HttpRequestException(
                $"API returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (body.Length > cfg.MaxResponseChars)
        {
            _log.LogWarning(
                "REST agent id={Id} response truncated from {Full} to {Max} chars.",
                cfg.Id, body.Length, cfg.MaxResponseChars);
            body = body[..cfg.MaxResponseChars] + "\n[Response truncated]";
        }

        return body;
    }
}
