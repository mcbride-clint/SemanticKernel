namespace BlazorAgentChat.Abstractions.Models;

/// <summary>
/// A user-uploaded file that has been processed: text extracted and an LLM summary generated.
/// Passed through the full orchestration pipeline so the router and every agent
/// can access the document content and summary.
/// </summary>
public sealed record AttachedDocument(
    string FileName,
    string ContentType,
    byte[] Bytes,
    string ExtractedText,   // full text if extractable; empty for pure-binary or vision-only files
    string Summary          // concise LLM-generated summary used in routing context
)
{
    public bool HasText  => !string.IsNullOrWhiteSpace(ExtractedText);
    public bool IsImage  => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsPdf    => ContentType == "application/pdf" ||
                            FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    public long SizeBytes => Bytes.LongLength;
}
