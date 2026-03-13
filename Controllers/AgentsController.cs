using BlazorAgentChat.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace BlazorAgentChat.Controllers;

[ApiController, Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly IAgentRegistry _registry;

    public AgentsController(IAgentRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var agents = _registry.GetAll()
            .Select(a => new { a.Id, a.Name, a.Description, a.SourceType });
        return Ok(agents);
    }
}
