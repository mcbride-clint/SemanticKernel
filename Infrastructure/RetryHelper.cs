using Microsoft.Extensions.Logging;

namespace BlazorAgentChat.Infrastructure;

/// <summary>
/// Simple retry helper with exponential backoff for transient LLM / HTTP failures.
/// Retries on HttpRequestException, TimeoutException, and TaskCanceledException
/// caused by timeouts (not user cancellation).
/// </summary>
public static class RetryHelper
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    /// <summary>
    /// Executes <paramref name="action"/> up to <paramref name="maxAttempts"/> times,
    /// retrying on transient errors with exponential backoff.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        ILogger                          logger,
        string                           operationName,
        int                              maxAttempts = 3,
        CancellationToken                ct          = default)
    {
        for (int attempt = 1; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (OperationCanceledException)
            {
                throw; // never retry explicit user cancellation
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                var delay = Delays[Math.Min(attempt - 1, Delays.Length - 1)];
                logger.LogWarning(
                    "Transient failure on attempt {Attempt}/{Max} for '{Op}'. " +
                    "Retrying in {DelayMs}ms. Error: {Msg}",
                    attempt, maxAttempts, operationName, (int)delay.TotalMilliseconds, ex.Message);
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt — let any exception propagate naturally
        return await action(ct);
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TimeoutException or TaskCanceledException;
}
