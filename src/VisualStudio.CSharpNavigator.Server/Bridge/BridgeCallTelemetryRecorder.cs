using System.Collections.Concurrent;

namespace VisualStudio.CSharpNavigator.Server.Bridge;

public sealed class BridgeCallTelemetryRecorder
{
    private readonly ConcurrentQueue<BridgeCallTelemetryEntry> _recent = new();
    private long _totalCallCount;
    private long _failedCallCount;

    public void Record(string method, long elapsedMilliseconds, long bridgeExecutionMilliseconds, bool succeeded)
    {
        Interlocked.Increment(ref _totalCallCount);
        if (!succeeded)
        {
            Interlocked.Increment(ref _failedCallCount);
        }

        _recent.Enqueue(new BridgeCallTelemetryEntry(method, elapsedMilliseconds, bridgeExecutionMilliseconds, succeeded));
        while (_recent.Count > 128 && _recent.TryDequeue(out _))
        {
        }
    }

    public BridgeCallTelemetrySummary GetSummary()
    {
        var recent = _recent.ToArray();
        return new BridgeCallTelemetrySummary(
            Interlocked.Read(ref _totalCallCount),
            Interlocked.Read(ref _failedCallCount),
            recent.Length == 0 ? 0 : (long)recent.Average(entry => entry.ElapsedMilliseconds),
            recent.Length == 0 ? 0 : recent.Max(entry => entry.ElapsedMilliseconds),
            recent.Length == 0 ? 0 : (long)recent.Average(entry => entry.BridgeExecutionMilliseconds));
    }

    private sealed record BridgeCallTelemetryEntry(string Method, long ElapsedMilliseconds, long BridgeExecutionMilliseconds, bool Succeeded);
}

public readonly record struct BridgeCallTelemetrySummary(
    long TotalCallCount,
    long FailedCallCount,
    long RecentAverageElapsedMilliseconds,
    long RecentMaxElapsedMilliseconds,
    long RecentAverageExecutionMilliseconds);
