using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Controllers.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BlazorAgentChat.Controllers;

[ApiController, Route("api/attachment")]
public class AttachmentController : ControllerBase
{
    private readonly IAttachmentProcessor _processor;

    public AttachmentController(IAttachmentProcessor processor)
    {
        _processor = processor;
    }

    /// <summary>
    /// Accepts a multipart/form-data file upload, processes it through the
    /// attachment pipeline (text extraction + LLM summary), and returns a DTO
    /// safe for JSON serialization and client-side storage.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> UploadAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        await using var stream = file.OpenReadStream();
        var doc = await _processor.ProcessAsync(file.FileName, file.ContentType, stream, ct);
        return Ok(new AttachedDocumentDto(doc));
    }
}
