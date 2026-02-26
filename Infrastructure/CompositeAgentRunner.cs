using BlazorAgentChat.Abstractions;
using BlazorAgentChat.Abstractions.Models;
using BlazorAgentChat.Infrastructure.Database;
using BlazorAgentChat.Infrastructure.Rest;
using BlazorAgentChat.Infrastructure.SemanticKernel;
using BlazorAgentChat.Infrastructure.TechnicalDrawing;
using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure;

/// <summary>
/// Dispatches RunAsync calls to the correct runner based on AgentInfo.SourceType.
/// This is the single IAgentRunner registered in DI — the orchestrator never
/// needs to know which backing source an agent uses.
///
/// To add a new source type, implement IAgentRunner, register it in Program.cs,
/// and add a case to the switch expression below.
/// </summary>
public sealed class CompositeAgentRunner : IAgentRunner
{
    private readonly SkAgentRunner                 _pdfRunner;
    private readonly DbAgentRunner                 _dbRunner;
    private readonly RestAgentRunner               _restRunner;
    private readonly TechnicalDrawingAgentRunner   _technicalDrawingRunner;
    private readonly ILogger<CompositeAgentRunner> _log;

    public CompositeAgentRunner(
        SkAgentRunner                  pdfRunner,
        DbAgentRunner                  dbRunner,
        RestAgentRunner                restRunner,
        TechnicalDrawingAgentRunner    technicalDrawingRunner,
        ILogger<CompositeAgentRunner>  log)
    {
        _pdfRunner              = pdfRunner;
        _dbRunner               = dbRunner;
        _restRunner             = restRunner;
        _technicalDrawingRunner = technicalDrawingRunner;
        _log                    = log;
    }

    public Task<AgentResponse> RunAsync(
        AgentInfo         agent,
        string            question,
        AttachedDocument? attachment = null,
        CancellationToken ct = default)
    {
        _log.LogDebug(
            "CompositeAgentRunner dispatching agent '{Name}' (id={Id}, source={Source}).",
            agent.Name, agent.Id, agent.SourceType);

        return agent.SourceType switch
        {
            "database"          => _dbRunner.RunAsync(agent, question, attachment, ct),
            "rest"              => _restRunner.RunAsync(agent, question, attachment, ct),
            "technical-drawing" => _technicalDrawingRunner.RunAsync(agent, question, attachment, ct),
            "pdf"               => _pdfRunner.RunAsync(agent, question, attachment, ct),
            _                   => _pdfRunner.RunAsync(agent, question, attachment, ct)   // safe default
        };
    }
}
