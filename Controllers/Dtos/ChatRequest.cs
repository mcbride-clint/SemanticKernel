using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Controllers.Dtos;

public sealed record ChatRequest(
    string Question,
    List<string>? EnabledAgentIds,
    List<ConversationTurn>? History,
    AttachedDocumentDto? Attachment);
