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

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string? ProjectName { get; set; }

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; }

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

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
