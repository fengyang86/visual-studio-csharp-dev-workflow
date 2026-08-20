using System;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum AgentWorkflowTaskKind
{
    Unknown = 0,
    EditTask = 1,
    BuildFailure = 2,
    RuntimeDebug = 3,
    ChangeReview = 4,
    Verification = 5,
    WorkspaceSetup = 6,
    Mutation = 7,
    AgentInstruction = 8,
}

public enum AgentWorkflowEvidenceKind
{
    Unknown = 0,
    BuildLog = 1,
    RoslynDiagnostic = 2,
    Symbol = 3,
    SourceSpan = 4,
    SourceSnippet = 5,
    DebugFrame = 6,
    Artifact = 7,
    VisualStudioDocument = 8,
    Inference = 9,
    Heuristic = 10,
    Workspace = 11,
}

public enum AgentWorkflowSafetySeverity
{
    Unknown = 0,
    Note = 1,
    Warning = 2,
    Blocker = 3,
}

public enum WorkflowResponseDetailLevel
{
    Compact = 0,
    Standard = 1,
    Full = 2,
}

public sealed class WorkspaceContextLease
{
    public string LeaseId { get; set; } = string.Empty;

    public VisualStudioBridgeTarget Target { get; set; } = new();

    public string SolutionPath { get; set; } = string.Empty;

    public DateTimeOffset IssuedUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }
}

public sealed class EvidencePacket
{
    public string TaskId { get; set; } = string.Empty;

    public AgentWorkflowTaskKind TaskKind { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public EvidenceItem[] PrimaryFindings { get; set; } = Array.Empty<EvidenceItem>();

    public EvidenceItem[] SupportingEvidence { get; set; } = Array.Empty<EvidenceItem>();

    public EvidenceResourceLink[] ResourceLinks { get; set; } = Array.Empty<EvidenceResourceLink>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public SafetyBlocker[] SafetyBlockers { get; set; } = Array.Empty<SafetyBlocker>();

    public string[] ResidualRisks { get; set; } = Array.Empty<string>();

    public WorkflowBudget Budget { get; set; } = new();

    public WorkflowTelemetry Telemetry { get; set; } = new();

    public bool IsPartial { get; set; }

    public string[] Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class EvidenceItem
{
    public string Id { get; set; } = string.Empty;

    public AgentWorkflowEvidenceKind Kind { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string SourceRef { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public SymbolDescriptor? Symbol { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string[] RelevanceReasons { get; set; } = Array.Empty<string>();

    public string Confidence { get; set; } = string.Empty;

    public bool IsPartial { get; set; }
}

public sealed class EvidenceResourceLink
{
    public string Uri { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public bool IsPartial { get; set; }
}

public sealed class WorkflowBudget
{
    public int MaxToolCalls { get; set; } = 6;

    public int MaxReturnedChars { get; set; } = 24000;

    public int MaxElapsedMilliseconds { get; set; } = 30000;

    public int MaxProjects { get; set; } = 20;

    public int MaxDiagnostics { get; set; } = 30;

    public int MaxReferences { get; set; } = 100;

    public bool AllowWholeSolution { get; set; }
}

public sealed class WorkflowTelemetry
{
    public string WorkflowName { get; set; } = string.Empty;

    public long ElapsedMilliseconds { get; set; }

    public int ReturnedItemCount { get; set; }

    public int ReturnedCharacterCount { get; set; }

    public int ResourceLinkCount { get; set; }

    public bool CacheHit { get; set; }

    public string[] PartialReasons { get; set; } = Array.Empty<string>();
}

public sealed class SafetyBlocker
{
    public string Code { get; set; } = string.Empty;

    public AgentWorkflowSafetySeverity Severity { get; set; }

    public string Message { get; set; } = string.Empty;

    public string SuggestedAction { get; set; } = string.Empty;
}

public sealed class CSharpEditTaskRequest
{
    public string ProblemText { get; set; } = string.Empty;

    public string? BuildOutput { get; set; }

    public string? BuildLogFilePath { get; set; }

    public string? SymbolQuery { get; set; }

    public string? FilePath { get; set; }

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string? ProjectName { get; set; }

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; } = CodeDiagnosticSeverity.Warning;

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

    public bool IncludeWholeSolutionDiagnostics { get; set; }

    public bool IncludeVisualStudioBuildOutput { get; set; } = true;

    public int MaxVisualStudioBuildOutputCharacters { get; set; } = 20000;

    public int MaxBuildIssues { get; set; } = 20;

    public int MaxDiagnostics { get; set; } = 30;

    public int MaxSymbols { get; set; } = 20;

    public int MaxRelatedItems { get; set; } = 20;

    public int MaxSourceSnippets { get; set; } = 8;

    public int ContextLines { get; set; } = 3;

    public int MaxCharsPerSnippet { get; set; } = 8000;

    public bool IncludeGeneratedCode { get; set; }

    public WorkflowResponseDetailLevel DetailLevel { get; set; } = WorkflowResponseDetailLevel.Compact;

    public string? WorkspaceContextLeaseId { get; set; }

    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class CSharpChangeReviewTaskRequest
{
    public string ProblemText { get; set; } = string.Empty;

    public string? BuildOutput { get; set; }

    public string? BuildLogFilePath { get; set; }

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string[] AreaPaths { get; set; } = Array.Empty<string>();

    public string? SymbolQuery { get; set; }

    public string? ProjectName { get; set; }

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; } = CodeDiagnosticSeverity.Warning;

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

    public int MaxBuildIssues { get; set; } = 20;

    public int MaxDiagnostics { get; set; } = 30;

    public int MaxSymbols { get; set; } = 20;

    public int MaxRelatedItems { get; set; } = 20;

    public int MaxFiles { get; set; } = 20;

    public bool IncludeGeneratedCode { get; set; }

    public WorkflowResponseDetailLevel DetailLevel { get; set; } = WorkflowResponseDetailLevel.Compact;

    public string? WorkspaceContextLeaseId { get; set; }

    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class CSharpVerificationRunTaskRequest
{
    public string ProblemText { get; set; } = string.Empty;

    public string? BuildOutput { get; set; }

    public string? BuildLogFilePath { get; set; }

    public string[] ChangedFiles { get; set; } = Array.Empty<string>();

    public string[] AreaPaths { get; set; } = Array.Empty<string>();

    public string? SymbolQuery { get; set; }

    public string? FilePath { get; set; }

    public string[] IncludePathPatterns { get; set; } = Array.Empty<string>();

    public string[] ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string? ProjectName { get; set; }

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; } = CodeDiagnosticSeverity.Warning;

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

    public bool IncludeVisualStudioBuildOutput { get; set; } = true;

    public int MaxVisualStudioBuildOutputCharacters { get; set; } = 20000;

    public int MaxBuildIssues { get; set; } = 20;

    public int MaxDiagnostics { get; set; } = 30;

    public int MaxRelatedTests { get; set; } = 20;

    public int MaxRelatedItems { get; set; } = 20;

    public bool IncludeGeneratedCode { get; set; }

    public WorkflowResponseDetailLevel DetailLevel { get; set; } = WorkflowResponseDetailLevel.Compact;

    public string? WorkspaceContextLeaseId { get; set; }

    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class CSharpRuntimeExceptionTaskRequest
{
    public string ProblemText { get; set; } = string.Empty;

    public string ExceptionText { get; set; } = string.Empty;

    public string[] ArtifactPaths { get; set; } = Array.Empty<string>();

    public string[] IncludeTextPatterns { get; set; } = Array.Empty<string>();

    public int MaxBreakpoints { get; set; } = 100;

    public int MaxFrames { get; set; } = 50;

    public int MaxSourceSnippets { get; set; } = 6;

    public int ContextLines { get; set; } = 3;

    public int MaxCharsPerSnippet { get; set; } = 8000;

    public bool IncludeGeneratedCode { get; set; }

    public WorkflowResponseDetailLevel DetailLevel { get; set; } = WorkflowResponseDetailLevel.Compact;

    public string? WorkspaceContextLeaseId { get; set; }

    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class CSharpEditTaskResult
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public EvidencePacket EvidencePacket { get; set; } = new();

    public CSharpTaskContextPackage? TaskContextPackage { get; set; }

    public WorkspaceContextLease? WorkspaceContextLease { get; set; }

    public bool ResponseBudgetExceeded { get; set; }

    public EditCandidate[] EditCandidates { get; set; } = Array.Empty<EditCandidate>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpRuntimeExceptionTaskResult
{
    public string Status { get; set; } = string.Empty;

    public EvidencePacket EvidencePacket { get; set; } = new();

    public DebugSessionPreparationPlan? DebugSession { get; set; }

    public ArtifactEvidenceReport? ArtifactEvidence { get; set; }

    public WorkspaceContextLease? WorkspaceContextLease { get; set; }

    public bool ResponseBudgetExceeded { get; set; }

    public DebugStackFrameInfo[] CallStack { get; set; } = Array.Empty<DebugStackFrameInfo>();

    public SourceContextSnippet[] SourceSnippets { get; set; } = Array.Empty<SourceContextSnippet>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class BuildFailureSession
{
    public string SessionId { get; set; } = string.Empty;

    public BuildIssue[] RootCauseCandidates { get; set; } = Array.Empty<BuildIssue>();

    public BuildIssue[] CascadeIssues { get; set; } = Array.Empty<BuildIssue>();

    public BuildFailureIssueBinding[] IssueBindings { get; set; } = Array.Empty<BuildFailureIssueBinding>();

    public BuildFailureProjectBinding[] ProjectBindings { get; set; } = Array.Empty<BuildFailureProjectBinding>();

    public CodeDiagnostic[] ScopedDiagnostics { get; set; } = Array.Empty<CodeDiagnostic>();

    public SourceContextSnippet[] SourceSnippets { get; set; } = Array.Empty<SourceContextSnippet>();

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public VerificationCommand[] RecommendedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VisualStudioOutputWindowSnapshot? BuildOutputWindow { get; set; }

    public VisualStudioErrorListItem[] ErrorListItems { get; set; } = Array.Empty<VisualStudioErrorListItem>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();
}

public sealed class BuildFailureIssueBinding
{
    public string IssueId { get; set; } = string.Empty;

    public BuildIssueKind IssueKind { get; set; }

    public SourceSpan? IssueSpan { get; set; }

    public string IssueProjectName { get; set; } = string.Empty;

    public string BoundProjectName { get; set; } = string.Empty;

    public string BoundProjectFilePath { get; set; } = string.Empty;

    public SymbolDescriptor? EnclosingSymbol { get; set; }

    public string EnclosingContextKind { get; set; } = string.Empty;

    public SourceSpan? EnclosingSpan { get; set; }

    public RelatedTestDescriptor[] RelatedTests { get; set; } = Array.Empty<RelatedTestDescriptor>();

    public string Confidence { get; set; } = string.Empty;

    public string[] BindingReasons { get; set; } = Array.Empty<string>();
}

public sealed class BuildFailureProjectBinding
{
    public string ProjectName { get; set; } = string.Empty;

    public string ProjectFilePath { get; set; } = string.Empty;

    public string[] IssueIds { get; set; } = Array.Empty<string>();

    public string[] BindingReasons { get; set; } = Array.Empty<string>();

    public string Confidence { get; set; } = string.Empty;
}

public sealed class CSharpChangeReviewTaskResult
{
    public string Status { get; set; } = string.Empty;

    public TaskContextSummary TaskContext { get; set; } = new();

    public EvidencePacket EvidencePacket { get; set; } = new();

    public CSharpChangeReviewReport? ChangeReviewReport { get; set; }

    public WorkspaceContextLease? WorkspaceContextLease { get; set; }

    public bool ResponseBudgetExceeded { get; set; }

    public AreaAuditFinding[] PublicApiRisks { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] TestGaps { get; set; } = Array.Empty<AreaAuditFinding>();

    public AreaAuditFinding[] CandidateEditLocations { get; set; } = Array.Empty<AreaAuditFinding>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpVerificationRunTaskResult
{
    public string Status { get; set; } = string.Empty;

    public EvidencePacket EvidencePacket { get; set; } = new();

    public CSharpRegressionScopePlan? RegressionScopePlan { get; set; }

    public WorkspaceContextLease? WorkspaceContextLease { get; set; }

    public bool ResponseBudgetExceeded { get; set; }

    public VerificationCommand[] SmokeCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VerificationCommand[] FocusedCommands { get; set; } = Array.Empty<VerificationCommand>();

    public VerificationCommand[] BroadCommands { get; set; } = Array.Empty<VerificationCommand>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class EditCandidate
{
    public string FilePath { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public SymbolDescriptor? Symbol { get; set; }

    public int Score { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Risk { get; set; } = string.Empty;

    public string VerificationHint { get; set; } = string.Empty;

    public EvidenceResourceLink? RequiredContextResource { get; set; }
}
