namespace StrataAI.Api;

internal static class ApiDiagnostics
{
    // Optional diagnostic providers must not replace request outcomes or the
    // original downstream failure. Never report a sink failure recursively.
    public static void Warning(ILogger logger, string message, params object?[] values)
    {
        try { logger.LogWarning(message, values); }
        catch (Exception) { }
    }

    public static void Warning(ILoggerFactory factory, string category, string message, params object?[] values)
    {
        // Creating a category logger can invoke an optional provider too.
        try { factory.CreateLogger(category).LogWarning(message, values); }
        catch (Exception) { }
    }

    public static void Error(ILogger logger, EventId id, string message, params object?[] values)
    {
        try { logger.LogError(id, message, values); }
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
