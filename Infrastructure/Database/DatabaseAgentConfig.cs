namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Shape of each entry in Data/DatabaseAgents/agents.json.
/// ContextQuery is a safe, predefined query that fetches data for the LLM to reason over.
/// The LLM never generates SQL — it only reads the results.
/// </summary>
public sealed record DatabaseAgentConfig(
    string Id,
    string Name,
    string Description,
    string ConnectionString,
    string ContextQuery,
    int    MaxRows    = 500,    // guard against flooding the LLM context window
    IReadOnlyList<DbAgentParameter>? Parameters = null
);
