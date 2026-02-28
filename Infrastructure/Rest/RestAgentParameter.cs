namespace BlazorAgentChat.Infrastructure.Rest;

/// <summary>
/// Describes a named parameter extracted from the user's question.
/// Location controls how it is injected into the HTTP request.
/// </summary>
public sealed record RestAgentParameter(
    string  Name,
    string  Description,
    bool    Required  = false,
    string  Location  = "query",   // "path" | "query" | "body"
    string? QueryKey  = null       // query-string key if different from Name (e.g. Name="UserId", QueryKey="user_id")
);
