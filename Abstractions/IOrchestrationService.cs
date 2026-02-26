using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IOrchestrationService
{
    /// <summary>
    /// Full pipeline: route → run agents → synthesize.
    /// Streams the final synthesized answer token-by-token.
    /// </summary>
    /// <param name="userQuestion">The user's natural-language question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="enabledAgentIds">
    /// Optional agent allowlist. Null means use all available agents.
    /// </param>
    /// <param name="attachment">
    /// Optional uploaded file. Its summary is passed to the router;
    /// its full content is passed to each runner that can use it.
    /// </param>
    IAsyncEnumerable<string> AskAsync(
        string                userQuestion,
        CancellationToken     ct              = default,
        IReadOnlySet<string>? enabledAgentIds = null,
        AttachedDocument?     attachment      = null);

    /// <summary>Expose last-run metadata for debug display.</summary>
    OrchestrationMetadata? LastMetadata { get; }
}
