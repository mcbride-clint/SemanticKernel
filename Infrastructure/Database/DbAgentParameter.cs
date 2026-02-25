namespace BlazorAgentChat.Infrastructure.Database;

/// <summary>
/// Describes a named parameter that the LLM will attempt to extract from the user's question.
/// The Name must match the @ParamName placeholder used in DatabaseAgentConfig.ContextQuery.
/// </summary>
public sealed record DbAgentParameter(
    string Name,
    string Description,
    bool   Required = false
);
