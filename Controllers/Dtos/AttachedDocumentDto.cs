using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Controllers.Dtos;

/// <summary>
/// JSON-serializable version of AttachedDocument (byte[] → Base64).
/// Sent from the client after /api/attachment upload and included in stream requests.
/// </summary>
public sealed record AttachedDocumentDto(
    string FileName,
    string ContentType,
    string Base64Bytes,
    string ExtractedText,
    string Summary)
{
    public AttachedDocumentDto(AttachedDocument doc)
        : this(doc.FileName, doc.ContentType,
               Convert.ToBase64String(doc.Bytes),
               doc.ExtractedText, doc.Summary) { }

    public AttachedDocument ToModel() => new(
        FileName, ContentType,
        Convert.FromBase64String(Base64Bytes),
        ExtractedText, Summary);
}
