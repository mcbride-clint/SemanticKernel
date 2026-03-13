using System.Diagnostics;
using System.Text;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Infrastructure.TechnicalDrawing;

/// <summary>
/// Extracts structured data from technical drawings and engineering documents supplied
/// as an attachment. Supports both text-based documents (PDF with extractable text) and
/// image-based drawings (uses LLM vision).
///
/// Extracted data includes:
///   • Title block (part/drawing number, revision, scale, author, date)
///   • Bill of Materials / Parts List
///   • Dimensions and tolerances
///   • Material and surface-finish specifications
///   • GD&amp;T callouts and manufacturing notes
///   • For spec sheets: key parameters, ratings, operating conditions
/// </summary>
public sealed class TechnicalDrawingAgentRunner : IAgentRunner
{
    private readonly TechnicalDrawingAgentLoader          _loader;
    private readonly KernelFactory                        _kernelFactory;
    private readonly ILogger<TechnicalDrawingAgentRunner> _log;

    private const string ExtractionSystemPrompt = """
        You are an expert at interpreting technical drawings, engineering specifications,
        CAD documents, and datasheets. Extract and present structured information from the
        provided document in a clear, organized format.

        For technical drawings, extract what is present:
        **Title Block**
        - Part number, drawing number, revision level
        - Project / assembly name, description
        - Scale, unit system (metric/imperial), sheet size
        - Author, date, approvals

        **Bill of Materials (BOM) / Parts List**
        Present as a table: Item | Part No. | Description | Material | Qty | Notes

        **Dimensions & Tolerances**
        - Critical and overall envelope dimensions
        - Tolerances (general and feature-specific)
        - Thread callouts, hole sizes

        **Material Specifications**
        - Base material with grade/standard (e.g., AISI 4140, Al 6061-T6)
        - Heat treatment, coatings, plating

        **Surface Finish**
        - Ra / Rz values, finish symbols

        **GD&T / Geometric Tolerances**
        - Feature control frames, datums

        **Manufacturing & Inspection Notes**
        - General tolerance block
        - Special processes, inspection requirements

        For specification sheets / datasheets, extract:
        - Product identification, model numbers
        - Key parameters and ratings (electrical, mechanical, thermal)
        - Operating / environmental conditions
        - Pin-out or interface descriptions if present

        Omit sections not present in the document. If data is unclear or illegible, note it.
        """;

    private const string NoAttachmentMessage =
        "I need a technical drawing or engineering document to analyze. " +
        "Please attach a PDF, image, or specification file to your message and ask your question again.";

    public TechnicalDrawingAgentRunner(
        TechnicalDrawingAgentLoader          loader,
        KernelFactory                        kernelFactory,
        ILogger<TechnicalDrawingAgentRunner> log)
    {
        _loader        = loader;
        _kernelFactory = kernelFactory;
        _log           = log;
    }

    public async Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        AttachedDocument? attachment = null,
        CancellationToken ct         = default)
    {
        _log.LogDebug(
            "Invoking TechnicalDrawingAgentRunner for agent '{Name}' (id={Id}). HasAttachment={Has}.",
            agent.Name, agent.Id, attachment is not null);

        if (attachment is null)
        {
            _log.LogWarning(
                "TechnicalDrawingAgentRunner called without attachment for agent '{Name}'.", agent.Name);
            return new AgentResponse(agent, NoAttachmentMessage, 0, TimeSpan.Zero);
        }

        var sw     = Stopwatch.StartNew();
        var kernel = _kernelFactory.Create();

        // Build the user message appropriate for the attachment type
        ChatMessageContent userMessage;

        if (attachment.IsImage)
        {
            // Vision path — pass raw image bytes to the LLM via a multi-part message
            _log.LogDebug(
                "Using vision path for image attachment '{File}' ({Bytes:N0} bytes).",
                attachment.FileName, attachment.SizeBytes);

            var items = new ChatMessageContentItemCollection
            {
                new TextContent(
                    $"Please extract all structured data from this technical document: {attachment.FileName}\n\n" +
                    $"User question: {question}"),
                new ImageContent(attachment.Bytes, mimeType: attachment.ContentType),
            };
            userMessage = new ChatMessageContent(AuthorRole.User, items);
        }
        else if (attachment.HasText)
        {
            // Text path — full extracted text in the prompt
            _log.LogDebug(
                "Using text path for '{File}' ({Chars} chars).",
                attachment.FileName, attachment.ExtractedText.Length);

            userMessage = new ChatMessageContent(
                AuthorRole.User,
                $"Document: {attachment.FileName}\n\n" +
                $"=== DOCUMENT CONTENT ===\n{attachment.ExtractedText}\n=== END DOCUMENT ===\n\n" +
                $"User question: {question}");
        }
        else
        {
            return new AgentResponse(
                agent,
                $"The attached file '{attachment.FileName}' could not be read. " +
                "Please ensure it is a PDF with extractable text, an image, or a text-based document.",
                0, sw.Elapsed);
        }

        var chatAgent = AgentKernelFactory.Create(
            agent.Name, ExtractionSystemPrompt, kernel, enableFunctions: false);

        try
        {
            // Each retry uses a fresh ChatHistoryAgentThread to avoid dirty state.
            var content = await RetryHelper.ExecuteAsync(async ck =>
            {
                var thread = new ChatHistoryAgentThread();
                var sb     = new StringBuilder();

                await foreach (var item in chatAgent.InvokeAsync(
                                   [userMessage], thread, options: null, ck))
                {
                    sb.Append(item.Message.Content);
                }

                return sb.ToString();
            }, _log, $"tech-drawing:{agent.Id}", maxAttempts: 3, ct);

            sw.Stop();

            _log.LogInformation(
                "TechnicalDrawingAgentRunner '{Name}' responded in {Ms}ms. Response length={Len}.",
                agent.Name, sw.ElapsedMilliseconds, content.Length);

            return new AgentResponse(
                Agent:           agent,
                Content:         content,
                EstimatedTokens: (attachment.ExtractedText.Length + content.Length) / 4,
                Elapsed:         sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _log.LogError(ex,
                "TechnicalDrawingAgentRunner failed for agent '{Name}' (id={Id}).", agent.Name, agent.Id);
            return new AgentResponse(
                agent,
                "I was unable to extract data from the document at this time.",
                0, sw.Elapsed);
        }
    }
}
