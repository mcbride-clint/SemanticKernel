using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Services;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure.Attachments;

/// <summary>
/// Reads an uploaded file stream, extracts text where possible, then calls
/// DocumentSummaryService to generate a routing-quality summary.
/// Supported input types:
///   PDF  → text via PdfTextExtractor
///   text/*, .csv, .json, .xml, .md → UTF-8 read
///   image/* → bytes stored; vision LLM produces the summary
///   other   → bytes stored; best-effort UTF-8 text read
/// </summary>
public sealed class AttachmentProcessor : IAttachmentProcessor
{
    private readonly PdfTextExtractor             _pdfExtractor;
    private readonly DocumentSummaryService       _summaryService;
    private readonly ILogger<AttachmentProcessor> _log;

    private const long MaxRecommendedBytes = 10L * 1024 * 1024; // 10 MB

    public AttachmentProcessor(
        PdfTextExtractor             pdfExtractor,
        DocumentSummaryService       summaryService,
        ILogger<AttachmentProcessor> log)
    {
        _pdfExtractor   = pdfExtractor;
        _summaryService = summaryService;
        _log            = log;
    }

    public async Task<AttachedDocument> ProcessAsync(
        string            fileName,
        string            contentType,
        Stream            content,
        CancellationToken ct = default)
    {
        _log.LogInformation(
            "Processing attachment '{File}' (type={Type}).", fileName, contentType);

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        if (bytes.LongLength > MaxRecommendedBytes)
            _log.LogWarning(
                "Attachment '{File}' is {Bytes:N0} bytes — may cause slow processing.", fileName, bytes.LongLength);

        var extractedText = ExtractText(fileName, contentType, bytes);

        _log.LogDebug(
            "Text extraction for '{File}': {Chars} chars.", fileName, extractedText.Length);

        var summary = await _summaryService.SummarizeAsync(
            fileName, contentType, extractedText, bytes, ct);

        _log.LogInformation(
            "Attachment '{File}' ready. HasText={Has}, SummaryLen={Len}",
            fileName, extractedText.Length > 0, summary.Length);

        return new AttachedDocument(fileName, contentType, bytes, extractedText, summary);
    }

    private string ExtractText(string fileName, string contentType, byte[] bytes)
    {
        try
        {
            if (contentType == "application/pdf" ||
                fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return _pdfExtractor.ExtractFromBytes(bytes);

            if (IsTextType(contentType, fileName))
                return System.Text.Encoding.UTF8.GetString(bytes);

            // Images are handled by vision in DocumentSummaryService — no text extraction here
            return string.Empty;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Text extraction failed for '{File}'. Proceeding without text.", fileName);
            return string.Empty;
        }
    }

    private static bool IsTextType(string contentType, string fileName)
    {
        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
        if (contentType is "application/json" or "application/xml") return true;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".txt" or ".csv" or ".json" or ".xml" or ".md";
    }
}
