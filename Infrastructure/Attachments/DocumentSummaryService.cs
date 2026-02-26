using System.Diagnostics;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure.Attachments;

/// <summary>
/// Generates a concise LLM summary of an uploaded document (text or image).
/// The summary is embedded in routing context so the router can pick the best agents.
/// </summary>
public sealed class DocumentSummaryService
{
    private readonly KernelFactory                   _kernelFactory;
    private readonly ILogger<DocumentSummaryService> _log;

    private const string SummarySystemPrompt = """
        You are a document analysis assistant. Analyze the provided document and respond in this exact format:

        Summary: [2-3 sentences describing what the document contains]
        Document type: [e.g., technical drawing, engineering specification, invoice, contract, report, datasheet, diagram]
        Key data: [comma-separated list of data categories present, e.g., dimensions, tolerances, part numbers, materials, dates, figures]
        Useful for: [brief examples of questions this document can help answer]

        Be concise. Do not add any text outside this format.
        """;

    public DocumentSummaryService(KernelFactory kernelFactory, ILogger<DocumentSummaryService> log)
    {
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    public async Task<string> SummarizeAsync(
        string            fileName,
        string            contentType,
        string            extractedText,
        byte[]            bytes,
        CancellationToken ct = default)
    {
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (!isImage && string.IsNullOrWhiteSpace(extractedText))
            return $"[Attached file: {fileName} — content could not be extracted for analysis]";

        var kernel      = _kernelFactory.Create();
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var history     = new ChatHistory();
        history.AddSystemMessage(SummarySystemPrompt);

        if (isImage)
        {
            // Use multimodal vision to describe the image
            var items = new ChatMessageContentItemCollection
            {
                new TextContent($"Please analyze this image file: {fileName}"),
                new ImageContent(bytes, mimeType: contentType)
            };
            history.Add(new ChatMessageContent(AuthorRole.User, items));
        }
        else
        {
            // Truncate to avoid overlong context in the summary call
            var truncated = extractedText.Length > 8000
                ? extractedText[..8000] + "\n[... truncated for summary ...]"
                : extractedText;
            history.AddUserMessage($"File: {fileName}\n\n{truncated}");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await RetryHelper.ExecuteAsync(
                async ck => await chatService.GetChatMessageContentAsync(history, cancellationToken: ck),
                _log, $"doc-summary:{fileName}", maxAttempts: 2, ct);

            _log.LogDebug(
                "Document summary for '{File}' generated in {Ms}ms.", fileName, sw.ElapsedMilliseconds);

            return result.Content?.Trim() ?? $"[Could not summarize {fileName}]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Document summary failed for '{File}'.", fileName);
            return $"[Attached file: {fileName}]";
        }
    }
}
