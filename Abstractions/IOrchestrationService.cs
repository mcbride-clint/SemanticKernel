using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IOrchestrationService
{
    /// <summary>
    /// Full pipeline: route → run agents → synthesize.
    /// Streams the final synthesized answer token-by-token.
    /// </summary>
    /// <param name="userQuestion">The user's question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="enabledAgentIds">
    /// Optional allowlist of agent IDs. When non-null only agents whose ID appears
    /// in this set are considered. Pass null to use all available agents.
    /// </param>
    IAsyncEnumerable<string> AskAsync(
        string                userQuestion,
        CancellationToken     ct              = default,
        IReadOnlySet<string>? enabledAgentIds = null);

    /// <summary>Expose last-run metadata for debug display.</summary>
    OrchestrationMetadata? LastMetadata { get; }
}
