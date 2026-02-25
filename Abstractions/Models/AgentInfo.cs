namespace BlazorAgentChat.Abstractions.Models;

/// <summary>
/// Immutable descriptor for a loaded agent. Contains no SK types.
/// SourceType distinguishes agent sources (e.g. "pdf", "database") for future extensibility.
/// </summary>
public sealed record AgentInfo(
    string Id,
    string Name,
    string Description,
    string PdfText,
    int    PdfCharCount,
    string SourceType = "pdf"
);
