using System.Collections.Concurrent;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class WorkspaceContextLeaseStore
{
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, WorkspaceContextLease> _leases = new(StringComparer.Ordinal);

    public WorkspaceContextLease Create(VisualStudioBridgeTarget target, string solutionPath)
    {
        RemoveExpired();
        var now = DateTimeOffset.UtcNow;
        var lease = new WorkspaceContextLease
        {
            LeaseId = $"workspace-{Guid.NewGuid():N}",
            Target = new VisualStudioBridgeTarget
            {
                PipeName = target.PipeName,
                InstanceId = target.InstanceId,
                SolutionPath = target.SolutionPath,
            },
            SolutionPath = solutionPath,
            IssuedUtc = now,
            ExpiresUtc = now.Add(DefaultTimeToLive),
        };
        _leases[lease.LeaseId] = lease;
        return lease;
    }

    public bool TryGet(string? leaseId, out WorkspaceContextLease lease)
    {
        lease = new WorkspaceContextLease();
        if (string.IsNullOrWhiteSpace(leaseId)
            || !_leases.TryGetValue(leaseId, out var cached))
        {
            return false;
        }

        if (cached.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            _leases.TryRemove(leaseId, out _);
            return false;
        }

        lease = cached;
        return true;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _leases)
        {
            if (pair.Value.ExpiresUtc <= now)
            {
                _leases.TryRemove(pair.Key, out _);
            }
        }
    }
}
