using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public sealed class CSharpInvestigationReport
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public NavigatorHealthReport Health { get; set; } = new();

    public BuildTriageReport? BuildTriage { get; set; }

    public PrimaryFile[] PrimaryFiles { get; set; } = Array.Empty<PrimaryFile>();

    public PrimarySymbol[] PrimarySymbols { get; set; } = Array.Empty<PrimarySymbol>();

    public PrimaryDiagnostic[] PrimaryDiagnostics { get; set; } = Array.Empty<PrimaryDiagnostic>();

    public SymbolImpactSummary? ImpactSummary { get; set; }

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public CodeDiagnostic[] Diagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public SymbolDescriptor[] Symbols { get; set; } = Array.Empty<SymbolDescriptor>();

    public SymbolDescriptor[] Definitions { get; set; } = Array.Empty<SymbolDescriptor>();

    public SymbolReference[] References { get; set; } = Array.Empty<SymbolReference>();

    public CallGraphEdge[] Callers { get; set; } = Array.Empty<CallGraphEdge>();

    public CallGraphEdge[] Callees { get; set; } = Array.Empty<CallGraphEdge>();

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpChangeReviewReport
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public WorkspaceStatus? WorkspaceStatus { get; set; }

    public BuildTriageReport? BuildTriage { get; set; }

    public PrimaryFile[] PrimaryFiles { get; set; } = Array.Empty<PrimaryFile>();

    public PrimarySymbol[] PrimarySymbols { get; set; } = Array.Empty<PrimarySymbol>();

    public PrimaryDiagnostic[] PrimaryDiagnostics { get; set; } = Array.Empty<PrimaryDiagnostic>();

    public SymbolImpactSummary? ImpactSummary { get; set; }

    public AreaAuditFinding[] Findings { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] PublicApiRisks { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] TestGaps { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] CandidateEditLocations { get; set; } = Array.Empty<AreaAuditFinding>();

    public CodeDiagnostic[] Diagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public SymbolDescriptor[] Symbols { get; set; } = Array.Empty<SymbolDescriptor>();

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public TemporaryMarker[] TemporaryMarkers { get; set; } = Array.Empty<TemporaryMarker>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpVerificationPlan
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public WorkspaceStatus? WorkspaceStatus { get; set; }

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public BuildTriageReport? BuildTriage { get; set; }

    public CodeDiagnostic[] Diagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public VerificationProject[] AffectedProjects { get; set; } = Array.Empty<VerificationProject>();

    public VerificationCommand[] RecommendedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpAreaAuditReport
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public WorkspaceStatus? WorkspaceStatus { get; set; }

    public PrimaryFile[] PrimaryFiles { get; set; } = Array.Empty<PrimaryFile>();

    public PrimarySymbol[] PrimarySymbols { get; set; } = Array.Empty<PrimarySymbol>();

    public PrimaryDiagnostic[] PrimaryDiagnostics { get; set; } = Array.Empty<PrimaryDiagnostic>();

    public AreaAuditFinding[] Findings { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] CoveredBehaviors { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] CoverageGaps { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] SuggestedTests { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] CandidateEditLocations { get; set; } = Array.Empty<AreaAuditFinding>();

    public CodeDiagnostic[] Diagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public SymbolDescriptor[] Symbols { get; set; } = Array.Empty<SymbolDescriptor>();

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public TemporaryMarker[] TemporaryMarkers { get; set; } = Array.Empty<TemporaryMarker>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpTaskContextPackage
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public NavigatorHealthReport Health { get; set; } = new();

    public BuildTriageReport? BuildTriage { get; set; }

    public PrimaryFile[] PrimaryFiles { get; set; } = Array.Empty<PrimaryFile>();

    public PrimarySymbol[] PrimarySymbols { get; set; } = Array.Empty<PrimarySymbol>();

    public PrimaryDiagnostic[] PrimaryDiagnostics { get; set; } = Array.Empty<PrimaryDiagnostic>();

    public SourceContextSnippet[] SourceSnippets { get; set; } = Array.Empty<SourceContextSnippet>();

    public SymbolImpactSummary? ImpactSummary { get; set; }

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpBuildFailureContext
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public EvidencePacket EvidencePacket { get; set; } = new();

    public BuildFailureSession BuildFailureSession { get; set; } = new();

    public BuildTriageReport? BuildTriage { get; set; }

    public CodeDiagnostic[] Diagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public VisualStudioOutputWindowSnapshot? BuildOutputWindow { get; set; }

    public VisualStudioErrorListItem[] ErrorListItems { get; set; } = Array.Empty<VisualStudioErrorListItem>();

    public SourceContextSnippet[] SourceSnippets { get; set; } = Array.Empty<SourceContextSnippet>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpRegressionScopePlan
{
    public string Status { get; set; } = string.Empty;

    public CSharpVerificationPlan VerificationPlan { get; set; } = new();

    public CSharpChangeReviewReport? ChangeReview { get; set; }

    public VerificationCommand[] SmokeCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VerificationCommand[] FocusedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VerificationCommand[] BroadCommands { get; set; } = Array.Empty<VerificationCommand>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class AreaAuditFinding
{
    public string Category { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string Confidence { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public sealed class TaskContextSummary
{
    public string ProblemText { get; set; } = string.Empty;

    public string TargetSolutionPath { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string SymbolQuery { get; set; } = string.Empty;

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string BuildEvidenceSource { get; set; } = string.Empty;

    public bool HasExplicitBuildInput { get; set; }

    public bool UsedVisualStudioBuildOutput { get; set; }

    public string TargetInstanceId { get; set; } = string.Empty;

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }
}

public sealed class PrimaryFile
{
    public string FilePath { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public int Score { get; set; }

    public int RelevanceScore { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public sealed class PrimarySymbol
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public SourceSpan? Span { get; set; }

    public int Score { get; set; }

    public int RelevanceScore { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public sealed class PrimaryDiagnostic
{
    public string Source { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public CodeDiagnosticSeverity Severity { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public bool IsLikelyCascade { get; set; }

    public bool IsBackgroundNoise { get; set; }

    public DiagnosticBaselineKind BaselineKind { get; set; }

    public int Score { get; set; }

    public int RelevanceScore { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public BuildIssueKind BuildIssueKind { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public enum DiagnosticBaselineKind
{
    Unknown = 0,
    IntroducedByCurrentChange = 1,
    PreExisting = 2,
    Cascade = 3,
    UnrelatedNoise = 4,
}

public sealed class VerificationProject
{
    public string ProjectName { get; set; } = string.Empty;

    public string ProjectFilePath { get; set; } = string.Empty;

    public bool IsTestProject { get; set; }

    public string[] Reasons { get; set; } = Array.Empty<string>();
}
