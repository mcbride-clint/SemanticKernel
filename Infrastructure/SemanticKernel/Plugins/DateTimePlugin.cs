using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace BlazorAgentChat.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// Example Semantic Kernel plugin that exposes date/time functions to the LLM.
/// Add [KernelFunction] methods here to give agents access to real-time data.
/// </summary>
public sealed class DateTimePlugin
{
    [KernelFunction("get_current_date")]
    [Description("Returns today's date in yyyy-MM-dd format (e.g. 2025-01-31).")]
    public string GetCurrentDate() =>
        DateTime.Now.ToString("yyyy-MM-dd");

    [KernelFunction("get_current_time")]
    [Description("Returns the current local time in HH:mm:ss format (e.g. 14:30:00).")]
    public string GetCurrentTime() =>
        DateTime.Now.ToString("HH:mm:ss");
}
