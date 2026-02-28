namespace BlazorAgentChat.Infrastructure.Rest;

/// <summary>
/// Shape of each entry in Data/RestAgents/agents.json.
///
/// UrlTemplate supports {ParamName} placeholders for path parameters.
/// Example: "https://api.example.com/v1/users/{UserId}/orders"
///
/// StaticHeaders is for auth (Bearer tokens, API keys, etc.) — values are read
/// from config at load time. Store secrets in appsettings.json or env vars, not here.
/// </summary>
public sealed record RestAgentConfig(
    string  Id,
    string  Name,
    string  Description,
    string  UrlTemplate,
    string  Method              = "GET",
    int     TimeoutSeconds      = 30,
    int     MaxResponseChars    = 8000,    // guard against flooding the LLM context window
    IReadOnlyDictionary<string, string>? StaticHeaders     = null,
    IReadOnlyDictionary<string, string>? StaticQueryParams = null,
    IReadOnlyList<RestAgentParameter>?   Parameters        = null,
    /// <summary>
    /// Optional JSON body template for POST/PUT requests.
    /// Use {ParamName} placeholders — values are replaced with extracted parameter values.
    /// Parameters with Location = "body" are substituted here.
    /// Example: {"query": "{SearchQuery}", "maxResults": 10}
    /// </summary>
    string? BodyTemplate = null
);
