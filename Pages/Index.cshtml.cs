using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorAgentChat.Pages;

public class IndexModel : PageModel
{
    public IReadOnlyList<AgentInfo> Agents { get; private set; } = [];

    public IndexModel(IAgentRegistry registry)
    {
        Agents = registry.GetAll();
    }

    public void OnGet() { }
}
