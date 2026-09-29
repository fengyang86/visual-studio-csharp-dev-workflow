using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum CodeDiagnosticSeverity
{
    Hidden = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

public enum CodeDiagnosticNoiseProfile
{
    Penalize = 0,
    Filter = 1,
    Off = 2,
    Auto = 3,
}

public enum CodeDiagnosticCollectionMode
{
    Auto = 0,
    Fast = 1,
    Complete = 2,
}

public enum CSharpVerificationCommandKind
{
    Build = 0,
    Test = 1,
}

public sealed class CSharpVerificationExecutionRequest
{
    public string TargetPath { get; set; } = string.Empty;

    public CSharpVerificationCommandKind Kind { get; set; } = CSharpVerificationCommandKind.Build;

    public string Configuration { get; set; } = "Release";

    public string? TestFilter { get; set; }

    public bool NoRestore { get; set; } = true;

    public int TimeoutMilliseconds { get; set; } = 300000;

    public int MaxOutputCharacters { get; set; } = 100000;

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();
}

public sealed class CSharpVerificationExecutionResult
{
    public CSharpVerificationCommandKind Kind { get; set; }

    public string TargetPath { get; set; } = string.Empty;

    public string CommandSummary { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public bool TimedOut { get; set; }

    public int ExitCode { get; set; } = -1;

    public int ElapsedMilliseconds { get; set; }

    public string Output { get; set; } = string.Empty;

    public bool IsOutputTruncated { get; set; }

    public BuildTriageReport Triage { get; set; } = new();
}

public enum BuildIssueKind
{
    Unknown = 0,
    Compiler = 1,
    Analyzer = 2,
    MsBuild = 3,
    NuGet = 4,
    Sdk = 5,
    Test = 6,
    Restore = 7,
    TargetFramework = 8,
    GeneratedOutput = 9,
}

public sealed class BuildTriageReport
{
    public BuildIssue[] Issues { get; set; } = Array.Empty<BuildIssue>();

    public int TotalIssueCount { get; set; }

    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    public int SuppressedIssueCount { get; set; }

    public int CascadeIssueCount { get; set; }

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class BuildIssue
{
    public string Id { get; set; } = string.Empty;

    public BuildIssueKind Kind { get; set; }

    public CodeDiagnosticSeverity Severity { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public string RawText { get; set; } = string.Empty;

    public int OccurrenceCount { get; set; } = 1;

    public bool IsLikelyCascade { get; set; }

    public bool IsInChangedFile { get; set; }

    public bool IsRootCauseCandidate { get; set; }

    public int RootCauseScore { get; set; }

    public string[] RankReasons { get; set; } = Array.Empty<string>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();
}

public sealed class DiagnosticsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? FilePath { get; set; }

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] NoisePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string? ProjectName { get; set; }

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; }

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

    public CodeDiagnosticCollectionMode CollectionMode { get; set; } = CodeDiagnosticCollectionMode.Auto;

    public int MaxResults { get; set; } = 500;

    public int MaxProjects { get; set; }

    public int MaxElapsedMilliseconds { get; set; } = 45000;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class ErrorListRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public int MaxResults { get; set; } = 100;
}

public sealed class VisualStudioErrorListItem
{
    public string Severity { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Column { get; set; }

    public string ProjectName { get; set; } = string.Empty;
}

public sealed class OutputWindowRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string PaneName { get; set; } = "Build";

    public int MaxCharacters { get; set; } = 20000;
}

public sealed class VisualStudioOutputWindowSnapshot
{
    public string PaneName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public int TotalCharacters { get; set; }

    public int ReturnedCharacters { get; set; }

    public bool IsTruncated { get; set; }
}

public sealed class CodeDiagnostic
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public CodeDiagnosticSeverity Severity { get; set; }

    public int WarningLevel { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public int RelevanceScore { get; set; }

    public string[] ScopeReasons { get; set; } = Array.Empty<string>();
}

public sealed class DiagnosticBaselineCapture
{
    public string BaselineId { get; set; } = string.Empty;
    public string WorkspaceVersion { get; set; } = string.Empty;
    public DateTimeOffset CapturedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public int DiagnosticCount { get; set; }
    public string ScopeFingerprint { get; set; } = string.Empty;
}

public sealed class DiagnosticBaselineComparison
{
    public string BaselineId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ScopeMatched { get; set; }
    public bool SnapshotChanged { get; set; }
    public string BaselineWorkspaceVersion { get; set; } = string.Empty;
    public string CurrentWorkspaceVersion { get; set; } = string.Empty;
    public DateTimeOffset BaselineCapturedUtc { get; set; }
    public DateTimeOffset BaselineExpiresUtc { get; set; }
    public CodeDiagnostic[] IntroducedDiagnostics { get; set; } = Array.Empty<CodeDiagnostic>();
    public CodeDiagnostic[] PreExistingDiagnostics { get; set; } = Array.Empty<CodeDiagnostic>();
    public CodeDiagnostic[] UnknownDiagnostics { get; set; } = Array.Empty<CodeDiagnostic>();
    public string[] RemovedDiagnosticKeys { get; set; } = Array.Empty<string>();
}

// Reads the diagnostics Visual Studio has already computed through its live
// background analysis (compiler + analyzers) instead of running a fresh
// synchronous analyzer pass. Near-instant on warm solutions, but completeness
// is best-effort while background analysis is pending.
public sealed class LiveDiagnosticsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? FilePath { get; set; }

    public string? ProjectName { get; set; }

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; }

    public int MaxResults { get; set; } = 200;

    public bool IncludeGeneratedCode { get; set; }
}
