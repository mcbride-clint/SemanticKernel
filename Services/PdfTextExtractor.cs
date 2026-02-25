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
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
            sb.AppendLine(page.Text);

        sw.Stop();
        var text = sb.ToString();

        _log.LogInformation(
            "Extracted {Chars} characters from {Pages} pages in {Ms}ms. File={Path}",
            text.Length, document.NumberOfPages, sw.ElapsedMilliseconds, Path.GetFileName(pdfPath));

        if (text.Length == 0)
            _log.LogWarning("PDF extraction returned empty text. File={Path}", pdfPath);

        return text;
    }
}
