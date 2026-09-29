using System.Text.Json;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public sealed class VersionedWorkspaceQueryCache
{
    // Every cached query pays an uncached snapshot-only status round trip just to
    // obtain the workspace version for the cache key. The status result is safe
    // to memoize briefly: the query results themselves are cached for 5s against
    // that same version, so 1.5s of extra staleness on the key lookup matches the
    // existing semantics.
    private const double DefaultStatusCacheMilliseconds = 1500;

    private readonly IVisualStudioWorkspaceBridge _bridge;
    private readonly ShortLivedQueryCache _cache;
    private readonly double _statusCacheMilliseconds;
    private readonly object _statusGate = new();
    private readonly Dictionary<string, StatusCacheEntry> _statusCache = new(StringComparer.Ordinal);

    private sealed class StatusCacheEntry
    {
        public WorkspaceQueryResult<WorkspaceStatus> Result = null!;
        public DateTimeOffset ReadAtUtc;
    }

    public VersionedWorkspaceQueryCache(
        IVisualStudioWorkspaceBridge bridge,
        ShortLivedQueryCache cache,
        TimeSpan? statusCacheTtl = null)
    {
        _bridge = bridge;
        _cache = cache;
        _statusCacheMilliseconds = statusCacheTtl?.TotalMilliseconds ?? DefaultStatusCacheMilliseconds;
    }

    private async Task<WorkspaceQueryResult<WorkspaceStatus>> GetSnapshotStatusAsync(
        VisualStudioBridgeTarget? target,
        CancellationToken cancellationToken)
    {
        var key = string.Join(
            "|",
            target?.PipeName ?? string.Empty,
            target?.InstanceId ?? string.Empty,
            target?.SolutionPath ?? string.Empty);
        lock (_statusGate)
        {
            if (_statusCache.TryGetValue(key, out var cached)
                && (DateTimeOffset.UtcNow - cached.ReadAtUtc).TotalMilliseconds < _statusCacheMilliseconds)
            {
                return cached.Result;
            }
        }

        var result = await _bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest { Target = target, SnapshotOnly = true },
            cancellationToken).ConfigureAwait(false);
        lock (_statusGate)
        {
            _statusCache[key] = new StatusCacheEntry { Result = result, ReadAtUtc = DateTimeOffset.UtcNow };
        }

        return result;
    }

    public async Task<CachedValue<WorkspaceQueryResult<T>>> QueryAsync<T>(
        string operation,
        IVisualStudioBridgeTargetedRequest request,
        Func<CancellationToken, Task<WorkspaceQueryResult<T>>> query,
        CancellationToken cancellationToken)
    {
        var statusResult = await GetSnapshotStatusAsync(request.Target, cancellationToken).ConfigureAwait(false);
        var status = statusResult.Items.FirstOrDefault();
        if (status is not null)
        {
            var known = new VisualStudioBridgeTarget
            {
                PipeName = request.Target?.PipeName ?? string.Empty,
                InstanceId = status.InstanceId,
                SolutionPath = status.SolutionPath,
            };
            if (WorkspaceTargetIdentity.Conflicts(request.Target ?? new VisualStudioBridgeTarget(), known))
            {
                return new CachedValue<WorkspaceQueryResult<T>>(new WorkspaceQueryResult<T>
                {
                    IsPartial = true,
                    Diagnostics = new[] { "WorkspaceTargetMismatch: The current Visual Studio workspace does not match the requested target; re-select the instance." },
                }, false);
            }

            // 将自动发现的目标固定下来，避免状态检查与查询分别选中不同实例。
            if (!string.IsNullOrWhiteSpace(status.InstanceId) && !string.IsNullOrWhiteSpace(status.SolutionPath))
            {
                request.Target = known;
            }

            if (request is DiagnosticsRequest diagnosticsRequest)
            {
                diagnosticsRequest.NoisePathPatterns =
                    WorkspaceWorkflowConfigurationLoader.LoadNoisePathPatterns(status.SolutionPath);
            }

            if (request is BatchSourceContextRequest batchSourceContextRequest)
            {
                batchSourceContextRequest.ExpectedWorkspaceVersion = status.WorkspaceVersion;
            }
        }

        if (status is null || !status.IsSolutionLoaded || statusResult.IsPartial
            || string.IsNullOrWhiteSpace(status.WorkspaceVersion))
        {
            // 旧桥接缺少快照身份时保留查询兼容性，但不复用可能陈旧的结果。
            return new CachedValue<WorkspaceQueryResult<T>>(
                await query(cancellationToken).ConfigureAwait(false), false);
        }

        var key = operation + ":" + status.WorkspaceVersion + ":"
            + JsonSerializer.Serialize(request, request.GetType());
        return await _cache.GetOrAddAsync(key, query, cancellationToken,
            shouldCache: result => !result.IsPartial).ConfigureAwait(false);
    }
}
