using System.Text.Json;
using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Controllers.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BlazorAgentChat.Controllers;

[ApiController, Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatController(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Streams the orchestration response as Server-Sent Events.
    /// Each event is one of:
    ///   data: "token"            — a streamed text token (JSON-encoded string)
    ///   data: [HISTORY]{json}   — updated ConversationTurn[] after completion
    ///   data: [METADATA]{json}  — OrchestrationMetadata after completion
    ///   data: [DONE]            — end-of-stream sentinel
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await using var scope = _scopeFactory.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrationService>();

        if (request.History is { Count: > 0 })
            orchestrator.RestoreHistory(request.History);

        IReadOnlySet<string>? enabledSet = request.EnabledAgentIds is { Count: > 0 }
            ? request.EnabledAgentIds.ToHashSet()
            : null;

        var attachment = request.Attachment?.ToModel();

        try
        {
            await foreach (var token in orchestrator.AskAsync(request.Question, ct, enabledSet, attachment))
            {
                await WriteEventAsync(JsonSerializer.Serialize(token), ct);
            }

            var history = orchestrator.GetHistory();
            await WriteEventAsync($"[HISTORY]{JsonSerializer.Serialize(history, _jsonOpts)}", ct);

            if (orchestrator.LastMetadata is { } meta)
                await WriteEventAsync($"[METADATA]{JsonSerializer.Serialize(meta, _jsonOpts)}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — normal
        }
        finally
        {
            await WriteEventAsync("[DONE]", ct);
        }
    }

    private async Task WriteEventAsync(string data, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
