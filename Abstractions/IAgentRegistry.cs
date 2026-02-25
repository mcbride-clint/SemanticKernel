using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

public interface IAgentRegistry
{
    /// <summary>Returns all registered agents. Safe to call from any thread.</summary>
    IReadOnlyList<AgentInfo> GetAll();

    AgentInfo? FindById(string id);
}
