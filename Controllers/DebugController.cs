using System.Diagnostics;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BlazorAgentChat.Controllers;

[ApiController, Route("api/debug")]
public class DebugController : ControllerBase
{
    private readonly KernelFactory _kernelFactory;

    public DebugController(KernelFactory kernelFactory)
    {
        _kernelFactory = kernelFactory;
    }

    /// <summary>
    /// Sends a minimal chat completion call to measure LLM round-trip latency.
    /// </summary>
    [HttpPost("ping")]
    public async Task<IActionResult> PingAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var kernel = _kernelFactory.Create();
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory();
            history.AddUserMessage("Reply with exactly: pong");
            var result = await chat.GetChatMessageContentAsync(history, cancellationToken: ct);
            sw.Stop();
            return Ok(new
            {
                success = true,
                elapsedMs = sw.ElapsedMilliseconds,
                reply = result.Content
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Ok(new
            {
                success = false,
                elapsedMs = sw.ElapsedMilliseconds,
                error = ex.Message
            });
        }
    }
}
