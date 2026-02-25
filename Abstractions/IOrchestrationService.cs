using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IOrchestrationService
{
    /// <summary>
    /// Full pipeline: route → run agents → synthesize.
    /// Streams the final synthesized answer token-by-token.
    /// </summary>
    IAsyncEnumerable<string> AskAsync(
        string            userQuestion,
        CancellationToken ct = default);

    /// <summary>Expose last-run metadata for debug display.</summary>
    OrchestrationMetadata? LastMetadata { get; }
}
