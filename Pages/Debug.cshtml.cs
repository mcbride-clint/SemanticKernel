using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorAgentChat.Pages;

public class DebugModel : PageModel
{
    public IReadOnlyList<AgentInfo> Agents { get; private set; } = [];

    public DebugModel(IAgentRegistry registry)
    {
        Agents = registry.GetAll();
    }

    public void OnGet() { }
}
