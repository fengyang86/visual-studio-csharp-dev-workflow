using System.Text.Json;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public sealed class DiagnosticBaselineStore
{
    private const int MaxEntries = 64;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public DiagnosticBaselineCapture Add(
        string workspaceVersion,
        DiagnosticsRequest request,
        IReadOnlyList<CodeDiagnostic> diagnostics,
        DateTimeOffset now)
    {
        var baselineId = "diagnostic-baseline:" + Guid.NewGuid().ToString("N");
        var expires = now.Add(Lifetime);
        var entry = new Entry(
            baselineId,
            workspaceVersion,
            CreateScopeFingerprint(request),
            now,
            expires,
            diagnostics.ToArray());
        lock (_gate)
        {
            RemoveExpired(now);
            _entries[baselineId] = entry;
            while (_entries.Count > MaxEntries)
            {
                var oldest = _entries.Values.OrderBy(item => item.CapturedUtc).First();
                _entries.Remove(oldest.BaselineId);
            }
        }

        return new DiagnosticBaselineCapture
        {
            BaselineId = baselineId,
            WorkspaceVersion = workspaceVersion,
            CapturedUtc = now,
            ExpiresUtc = expires,
            DiagnosticCount = diagnostics.Count,
            ScopeFingerprint = entry.ScopeFingerprint,
        };
    }

    public bool TryGet(string baselineId, DateTimeOffset now, out Entry? entry, out string failure)
    {
        lock (_gate)
        {
            RemoveExpired(now);
            if (!_entries.TryGetValue(baselineId, out entry))
            {
                failure = "DiagnosticBaselineNotFoundOrExpired: capture a new baseline and retry.";
                return false;
            }
        }

        failure = string.Empty;
        return true;
    }

    public static string CreateScopeFingerprint(DiagnosticsRequest request)
    {
        var scope = new DiagnosticsRequest
        {
            FilePath = request.FilePath,
            IncludePathPatterns = request.IncludePathPatterns ?? Array.Empty<string>(),
            ExcludePathPatterns = request.ExcludePathPatterns ?? Array.Empty<string>(),
            NoisePathPatterns = request.NoisePathPatterns ?? Array.Empty<string>(),
            ChangedFiles = request.ChangedFiles ?? Array.Empty<string>(),
            ProjectName = request.ProjectName,
            MinimumSeverity = request.MinimumSeverity,
            NoiseProfile = request.NoiseProfile,
            CollectionMode = request.CollectionMode,
            MaxResults = request.MaxResults,
            MaxProjects = request.MaxProjects,
            MaxElapsedMilliseconds = request.MaxElapsedMilliseconds,
            IncludeGeneratedCode = request.IncludeGeneratedCode,
        };
        return JsonSerializer.Serialize(scope);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in _entries
                     .Where(pair => pair.Value.ExpiresUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _entries.Remove(key);
        }
    }

    public sealed record Entry(
        string BaselineId,
        string WorkspaceVersion,
        string ScopeFingerprint,
        DateTimeOffset CapturedUtc,
        DateTimeOffset ExpiresUtc,
        CodeDiagnostic[] Diagnostics);
}
