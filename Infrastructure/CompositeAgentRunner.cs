using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure.Database;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure;

/// <summary>
/// Dispatches RunAsync calls to the correct runner based on AgentInfo.SourceType.
/// This is the single IAgentRunner registered in DI — the orchestrator never
/// needs to know which backing source an agent uses.
///
/// To add a new source type (e.g. "api"), implement IAgentRunner and add a
/// case to the switch expression below.
/// </summary>
public sealed class CompositeAgentRunner : IAgentRunner
{
    private readonly SkAgentRunner          _pdfRunner;
    private readonly DbAgentRunner          _dbRunner;
    private readonly ILogger<CompositeAgentRunner> _log;

    public CompositeAgentRunner(
        SkAgentRunner                  pdfRunner,
        DbAgentRunner                  dbRunner,
        ILogger<CompositeAgentRunner>  log)
    {
        _pdfRunner = pdfRunner;
        _dbRunner  = dbRunner;
        _log       = log;
    }

    public Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        CancellationToken ct = default)
    {
        _log.LogDebug(
            "CompositeAgentRunner dispatching agent '{Name}' (id={Id}, source={Source}).",
            agent.Name, agent.Id, agent.SourceType);

        return agent.SourceType switch
        {
            "database" => _dbRunner.RunAsync(agent, question, ct),
            "pdf"      => _pdfRunner.RunAsync(agent, question, ct),
            _          => _pdfRunner.RunAsync(agent, question, ct)   // safe default
        };
    }
}
