using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class MutationSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MutationSessionRecord> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;
    private readonly int _capacity;

    public MutationSessionStore()
        : this(TimeSpan.FromMinutes(20), 128)
    {
    }

    public MutationSessionStore(TimeSpan ttl, int capacity)
    {
        _ttl = ttl;
        _capacity = Math.Max(1, capacity);
    }

    public void Record(WorkspaceMutationPreview preview)
    {
        if (string.IsNullOrWhiteSpace(preview.SessionId))
        {
            return;
        }

        var record = new MutationSessionRecord(
            preview.SessionId,
            preview.WorkspaceVersion,
            preview.CandidateIdentity,
            preview.Blockers.ToArray(),
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            RemoveExpired_NoLock(record.CreatedAt);
            _sessions[record.SessionId] = record;
            Trim_NoLock();
        }
    }

    public bool TryGet(string sessionId, out MutationSessionRecord? record)
    {
        lock (_gate)
        {
            RemoveExpired_NoLock(DateTimeOffset.UtcNow);
            return _sessions.TryGetValue(sessionId, out record);
        }
    }

    public MutationSessionValidationResult Validate(
        CSharpCodeFixRequest request,
        WorkspaceMutationPreview currentPreview)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewSessionId))
        {
            return MutationSessionValidationResult.Blocked(
                "MutationSessionRequired: apply requires PreviewSessionId from a recent mutation preview result.");
        }

        MutationSessionRecord? record;
        lock (_gate)
        {
            RemoveExpired_NoLock(DateTimeOffset.UtcNow);
            _sessions.TryGetValue(request.PreviewSessionId!, out record);
        }

        if (record is null)
        {
            return MutationSessionValidationResult.Blocked(
                "MutationSessionNotFound: preview session was not found or has expired. Re-run preview_csharp_code_fix.");
        }

        if (!string.Equals(record.SessionId, currentPreview.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return MutationSessionValidationResult.Blocked(
                "MutationSessionMismatch: current preview session id differs from the stored preview session.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedWorkspaceVersion)
            && !string.Equals(request.ExpectedWorkspaceVersion, currentPreview.WorkspaceVersion, StringComparison.Ordinal))
        {
            return MutationSessionValidationResult.Blocked(
                "WorkspaceVersionMismatch: request.ExpectedWorkspaceVersion differs from the current preview workspace version.");
        }

        if (!string.Equals(record.WorkspaceVersion, currentPreview.WorkspaceVersion, StringComparison.Ordinal))
        {
            return MutationSessionValidationResult.Blocked(
                "WorkspaceVersionChanged: workspace version differs from the stored preview session. Re-run the matching preview tool.");
        }

        if (!CandidateEquals(record.CandidateIdentity, currentPreview.CandidateIdentity))
        {
            return MutationSessionValidationResult.Blocked(
                "MutationCandidateChanged: candidate identity differs from the stored preview session. Re-run list_csharp_code_fixes and the matching preview tool.");
        }

        if (record.Blockers.Length > 0 || currentPreview.Blockers.Count > 0)
        {
            return MutationSessionValidationResult.Blocked(
                "MutationPreviewBlocked: preview has safety blockers and cannot be applied by default.");
        }

        return MutationSessionValidationResult.Allowed(record);
    }

    private static bool CandidateEquals(MutationCandidateIdentity left, MutationCandidateIdentity right)
    {
        return string.Equals(left.DiagnosticId, right.DiagnosticId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ProviderName, right.ProviderName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.EquivalenceKey, right.EquivalenceKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Scope, right.Scope, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.DocumentOrProject, right.DocumentOrProject, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.StableKey, right.StableKey, StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveExpired_NoLock(DateTimeOffset now)
    {
        var expired = _sessions.Values
            .Where(record => now - record.CreatedAt > _ttl)
            .Select(record => record.SessionId)
            .ToArray();

        foreach (var sessionId in expired)
        {
            _sessions.Remove(sessionId);
        }
    }

    private void Trim_NoLock()
    {
        if (_sessions.Count <= _capacity)
        {
            return;
        }

        var overflow = _sessions.Count - _capacity;
        foreach (var sessionId in _sessions.Values
            .OrderBy(record => record.CreatedAt)
            .Take(overflow)
            .Select(record => record.SessionId)
            .ToArray())
        {
            _sessions.Remove(sessionId);
        }
    }
}

public sealed record MutationSessionRecord(
    string SessionId,
    string WorkspaceVersion,
    MutationCandidateIdentity CandidateIdentity,
    WorkspaceMutationBlocker[] Blockers,
    DateTimeOffset CreatedAt);

public sealed class MutationSessionValidationResult
{
    private MutationSessionValidationResult(bool isAllowed, MutationSessionRecord? record, string? blocker)
    {
        IsAllowed = isAllowed;
        Record = record;
        Blocker = blocker;
    }

    public bool IsAllowed { get; }

    public MutationSessionRecord? Record { get; }

    public string? Blocker { get; }

    public static MutationSessionValidationResult Allowed(MutationSessionRecord record)
    {
        return new MutationSessionValidationResult(true, record, null);
    }

    public static MutationSessionValidationResult Blocked(string blocker)
    {
        return new MutationSessionValidationResult(false, null, blocker);
    }
}
