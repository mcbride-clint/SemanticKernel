using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace BlazorAgentChat.Infrastructure.SemanticKernel;

/// <summary>
/// Factory helpers for building <see cref="ChatCompletionAgent"/> instances from
/// a system prompt and a pre-configured <see cref="Kernel"/>.
///
/// Centralises agent construction so that every runner gets consistent settings
/// (execution settings, function-choice behaviour) without duplication.
/// </summary>
internal static class AgentKernelFactory
{
    // Agent names sent to the API must match ^[a-zA-Z0-9_-]{1,64}$
    private static readonly Regex _unsafeChars = new(@"[^a-zA-Z0-9_\-]", RegexOptions.Compiled);

    /// <summary>
    /// Creates a <see cref="ChatCompletionAgent"/> that uses the given
    /// <paramref name="systemPrompt"/> as its <see cref="ChatCompletionAgent.Instructions"/>.
    /// </summary>
    /// <param name="agentName">
    ///   Human-readable name surfaced in logs and OpenAI request headers.
    ///   Sanitised to contain only <c>[a-zA-Z0-9_-]</c>.
    /// </param>
    /// <param name="systemPrompt">The system prompt / instructions for this agent.</param>
    /// <param name="kernel">
    ///   A <see cref="Kernel"/> already configured with the chat-completion service
    ///   and any registered plugins.
    /// </param>
    /// <param name="enableFunctions">
    ///   When <see langword="true"/> (default) the agent uses
    ///   <see cref="FunctionChoiceBehavior.Auto()"/> so it can invoke
    ///   <see cref="KernelFunction"/> plugins.  Set to <see langword="false"/>
    ///   for routing / data-interpretation calls where tool use is not needed.
    /// </param>
    internal static ChatCompletionAgent Create(
        string  agentName,
        string  systemPrompt,
        Kernel  kernel,
        bool    enableFunctions = true)
    {
        var safeName = SanitizeName(agentName);

        var executionSettings = enableFunctions
            ? new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() }
            : new OpenAIPromptExecutionSettings();

        return new ChatCompletionAgent
        {
            Name         = safeName,
            Instructions = systemPrompt,
            Kernel       = kernel,
            Arguments    = new KernelArguments(executionSettings),
        };
    }

    /// <summary>
    /// Strips characters that are invalid in agent names and truncates to 64 chars.
    /// Falls back to <c>"agent"</c> if the result would be empty.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var safe = _unsafeChars.Replace(name, "_");
        if (safe.Length > 64) safe = safe[..64];
        return string.IsNullOrEmpty(safe) ? "agent" : safe;
    }
}
