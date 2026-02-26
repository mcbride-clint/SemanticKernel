using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace BlazorAgentChat.Services;

public sealed class PdfTextExtractor
{
    private readonly ILogger<PdfTextExtractor> _log;

    public PdfTextExtractor(ILogger<PdfTextExtractor> log) => _log = log;

    public string Extract(string pdfPath)
    {
        _log.LogDebug("Extracting text from PDF: {Path}", pdfPath);
        var sw = Stopwatch.StartNew();

        using var document = PdfDocument.Open(pdfPath);
        var text = ReadPages(document);
        sw.Stop();

        _log.LogInformation(
            "Extracted {Chars} characters from {Pages} pages in {Ms}ms. File={Path}",
            text.Length, document.NumberOfPages, sw.ElapsedMilliseconds, Path.GetFileName(pdfPath));

        if (text.Length == 0)
            _log.LogWarning("PDF extraction returned empty text. File={Path}", pdfPath);

        return text;
    }

    /// <summary>Extracts text from a PDF supplied as raw bytes (e.g., an uploaded file).</summary>
    public string ExtractFromBytes(byte[] pdfBytes)
    {
        _log.LogDebug("Extracting text from in-memory PDF ({Bytes:N0} bytes).", pdfBytes.Length);
        var sw = Stopwatch.StartNew();

        using var document = PdfDocument.Open(pdfBytes);
        var text = ReadPages(document);
        sw.Stop();

        _log.LogInformation(
            "Extracted {Chars} chars from {Pages} pages in {Ms}ms (in-memory PDF).",
            text.Length, document.NumberOfPages, sw.ElapsedMilliseconds);

        if (text.Length == 0)
            _log.LogWarning("PDF extraction returned empty text for in-memory PDF.");

        return text;
    }

    private static string ReadPages(UglyToad.PdfPig.PdfDocument document)
    {
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}
