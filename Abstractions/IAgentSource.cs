using BlazorAgentChat.Abstractions.Models;

namespace BlazorAgentChat.Abstractions;

/// <summary>
/// Implemented by any class that can produce a list of agents for the registry.
/// Register multiple implementations (PDF, database, API, etc.) — the registry
/// collects and merges them all automatically via IEnumerable&lt;IAgentSource&gt;.
/// </summary>
public interface IAgentSource
{
    List<AgentInfo> LoadAll();
}
