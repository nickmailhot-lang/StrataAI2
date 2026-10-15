using Microsoft.Extensions.Logging;

namespace StrataAI.Worker;

internal static class WorkerLog
{
    // Preserve existing structured messages, but never let an optional provider
    // replace job results or fault a background service. Do not log recursively.
    public static void Information(ILogger logger, string message, params object?[] values)
    {
        try { logger.LogInformation(message, values); }
        catch (Exception) { }
    }
    public static void Warning(ILogger logger, string message, params object?[] values)
    {
        try { logger.LogWarning(message, values); }
        catch (Exception) { }
    }
    public static IDisposable? BeginScope(ILogger logger, Dictionary<string, object?> state)
    {
        try { return logger.BeginScope(state) is { } scope ? new SafeScope(scope) : null; }
        catch (Exception) { return null; }
    }
    private sealed class SafeScope(IDisposable inner) : IDisposable
    {
        private IDisposable? _inner = inner;
        public void Dispose()
        {
            var scope = Interlocked.Exchange(ref _inner, null);
            try { scope?.Dispose(); }
            catch (Exception) { }
        }
    }
}
