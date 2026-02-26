using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

/// <summary>
/// Reads a file stream, extracts text (when possible), and generates an LLM summary.
/// The resulting AttachedDocument is ready to be passed into the orchestration pipeline.
/// </summary>
public interface IAttachmentProcessor
{
    Task<AttachedDocument> ProcessAsync(
        string            fileName,
        string            contentType,
        Stream            content,
        CancellationToken ct = default);
}
