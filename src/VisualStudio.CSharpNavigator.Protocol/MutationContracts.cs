using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum WorkspaceMutationKind
{
    Unknown = 0,
    Rename = 1,
    Cleanup = 2,
    CodeFix = 3,
    FixAll = 4,
    Refactoring = 5,
}

public enum WorkspaceMutationBlockerKind
{
    Unknown = 0,
    Conflict = 1,
    TruncatedPreview = 2,
    GeneratedDocumentChanges = 3,
    UnsupportedDocumentChanges = 4,
    TargetRequired = 5,
    WorkspaceChanged = 6,
    ApplyRejected = 7,
    UnsupportedOperation = 8,
    InvalidScope = 9,
    CandidateAmbiguous = 10,
    ProviderUnavailable = 11,
}

public enum CSharpCleanupOperation
{
    Format = 1,
    OrganizeUsings = 2,
    Simplify = 3,
}

public enum CSharpMutationScopeKind
{
    Unknown = 0,
    Document = 1,
    Project = 2,
    ChangedFiles = 3,
    Solution = 4,
}

public enum CSharpRefactoringPlanKind
{
    Unknown = 0,
    ExtractMethod = 1,
    ChangeSignature = 2,
    MoveType = 3,
    Rename = 4,
    Cleanup = 5,
    CodeFix = 6,
}

public sealed class RenamePreviewRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public string NewName { get; set; } = string.Empty;

    public bool RenameOverloads { get; set; }

    public bool RenameInStrings { get; set; }

    public bool RenameInComments { get; set; }

    public bool RenameFile { get; set; }

    public int MaxTextChanges { get; set; } = 1000;

    public int MaxSnippetLength { get; set; } = 200;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class RenameApplyRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public SymbolKey? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public string NewName { get; set; } = string.Empty;

    public bool RenameOverloads { get; set; }

    public bool RenameInStrings { get; set; }

    public bool RenameInComments { get; set; }

    public bool RenameFile { get; set; }

    public int MaxTextChanges { get; set; } = 1000;

    public int MaxSnippetLength { get; set; } = 200;

    public bool IncludeGeneratedCode { get; set; }

    public bool AllowConflicts { get; set; }

    public bool AllowGeneratedDocumentChanges { get; set; }

    public bool AllowUnsupportedDocumentChanges { get; set; }

    public bool AllowTruncatedPreview { get; set; }
}

public sealed class RenameTextChange
{
    public SourceSpan OldSpan { get; set; } = new();

    public SourceSpan NewSpan { get; set; } = new();

    public string OldText { get; set; } = string.Empty;

    public string NewText { get; set; } = string.Empty;

    public bool IsSnippetTruncated { get; set; }
}

public sealed class RenameDocumentPreview
{
    public string ProjectName { get; set; } = string.Empty;

    public string OldDocumentName { get; set; } = string.Empty;

    public string NewDocumentName { get; set; } = string.Empty;

    public string OldFilePath { get; set; } = string.Empty;

    public string NewFilePath { get; set; } = string.Empty;

    public bool IsGenerated { get; set; }

    public bool IsDocumentRename { get; set; }

    public int TextChangeCount { get; set; }

    public int ReturnedTextChangeCount { get; set; }

    public IReadOnlyList<RenameTextChange> TextChanges { get; set; } = Array.Empty<RenameTextChange>();
}

public sealed class RenameConflict
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }
}

public sealed class WorkspaceMutationTextChange
{
    public SourceSpan OldSpan { get; set; } = new();

    public SourceSpan NewSpan { get; set; } = new();

    public string OldText { get; set; } = string.Empty;

    public string NewText { get; set; } = string.Empty;

    public bool IsSnippetTruncated { get; set; }
}

public sealed class WorkspaceMutationDocumentPreview
{
    public string ProjectName { get; set; } = string.Empty;

    public string OldDocumentName { get; set; } = string.Empty;

    public string NewDocumentName { get; set; } = string.Empty;

    public string OldFilePath { get; set; } = string.Empty;

    public string NewFilePath { get; set; } = string.Empty;

    public bool IsGenerated { get; set; }

    public bool IsDocumentRename { get; set; }

    public int TextChangeCount { get; set; }

    public int ReturnedTextChangeCount { get; set; }

    public IReadOnlyList<WorkspaceMutationTextChange> TextChanges { get; set; } = Array.Empty<WorkspaceMutationTextChange>();
}

public sealed class WorkspaceMutationConflict
{
    public string ProjectName { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }
}

public sealed class WorkspaceMutationBlocker
{
    public WorkspaceMutationBlockerKind Kind { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class WorkspaceMutationPreview
{
    public string SessionId { get; set; } = string.Empty;

    public string WorkspaceVersion { get; set; } = string.Empty;

    public MutationCandidateIdentity CandidateIdentity { get; set; } = new();

    public WorkspaceMutationKind Kind { get; set; }

    public string OperationName { get; set; } = string.Empty;

    public bool HasConflicts { get; set; }

    public bool IsTruncated { get; set; }

    public int AffectedDocumentCount { get; set; }

    public int ReturnedDocumentCount { get; set; }

    public int OmittedGeneratedDocumentCount { get; set; }

    public int UnsupportedDocumentChangeCount { get; set; }

    public int TotalTextChangeCount { get; set; }

    public int ReturnedTextChangeCount { get; set; }

    public IReadOnlyList<WorkspaceMutationDocumentPreview> Documents { get; set; } = Array.Empty<WorkspaceMutationDocumentPreview>();

    public IReadOnlyList<WorkspaceMutationConflict> Conflicts { get; set; } = Array.Empty<WorkspaceMutationConflict>();

    public IReadOnlyList<WorkspaceMutationBlocker> Blockers { get; set; } = Array.Empty<WorkspaceMutationBlocker>();
}

public sealed class WorkspaceMutationApplyResult
{
    public bool Applied { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string WorkspaceVersion { get; set; } = string.Empty;

    public WorkspaceMutationPreview Preview { get; set; } = new();

    public string ApplyFailure { get; set; } = string.Empty;
}

public sealed class MutationCandidateIdentity
{
    public string DiagnosticId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string EquivalenceKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string DocumentOrProject { get; set; } = string.Empty;

    public string StableKey { get; set; } = string.Empty;
}

public sealed class RenamePreview
{
    public SymbolDescriptor Symbol { get; set; } = new();

    public string NewName { get; set; } = string.Empty;

    public bool RenameOverloads { get; set; }

    public bool RenameInStrings { get; set; }

    public bool RenameInComments { get; set; }

    public bool RenameFile { get; set; }

    public bool TargetIsGenerated { get; set; }

    public bool TargetIsMetadata { get; set; }

    public bool TargetIsPartial { get; set; }

    public bool HasConflicts { get; set; }

    public bool IsTruncated { get; set; }

    public int AffectedDocumentCount { get; set; }

    public int ReturnedDocumentCount { get; set; }

    public int OmittedGeneratedDocumentCount { get; set; }

    public int UnsupportedDocumentChangeCount { get; set; }

    public int TotalTextChangeCount { get; set; }

    public int ReturnedTextChangeCount { get; set; }

    public IReadOnlyList<RenameDocumentPreview> Documents { get; set; } = Array.Empty<RenameDocumentPreview>();

    public IReadOnlyList<RenameConflict> Conflicts { get; set; } = Array.Empty<RenameConflict>();

    public WorkspaceMutationPreview MutationPreview { get; set; } = new();
}

public sealed class RenameApplyResult
{
    public bool Applied { get; set; }

    public RenamePreview Preview { get; set; } = new();

    public string ApplyFailure { get; set; } = string.Empty;

    public WorkspaceMutationApplyResult MutationResult { get; set; } = new();
}

public class CSharpCleanupRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public CSharpMutationScopeKind ScopeKind { get; set; } = CSharpMutationScopeKind.Document;

    public string? FilePath { get; set; }

    public string? ProjectName { get; set; }

    public IReadOnlyList<string> ChangedFiles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<CSharpCleanupOperation> Operations { get; set; } = Array.Empty<CSharpCleanupOperation>();

    public int MaxTextChanges { get; set; } = 1000;

    public int MaxSnippetLength { get; set; } = 200;

    public bool IncludeGeneratedCode { get; set; }

    public bool AllowSolutionScope { get; set; }
}

public sealed class CSharpCleanupApplyRequest : CSharpCleanupRequest
{
    public bool AllowGeneratedDocumentChanges { get; set; }

    public bool AllowUnsupportedDocumentChanges { get; set; }

    public bool AllowTruncatedPreview { get; set; }
}

public sealed class CSharpCleanupPreview
{
    public CSharpMutationScopeKind ScopeKind { get; set; }

    public IReadOnlyList<string> ScopeValues { get; set; } = Array.Empty<string>();

    public IReadOnlyList<CSharpCleanupOperation> Operations { get; set; } = Array.Empty<CSharpCleanupOperation>();

    public WorkspaceMutationPreview MutationPreview { get; set; } = new();
}

public sealed class CSharpCleanupApplyResult
{
    public bool Applied { get; set; }

    public CSharpCleanupPreview Preview { get; set; } = new();

    public string ApplyFailure { get; set; } = string.Empty;

    public WorkspaceMutationApplyResult MutationResult { get; set; } = new();
}

public sealed class CSharpCodeFixListRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public CSharpMutationScopeKind ScopeKind { get; set; } = CSharpMutationScopeKind.Document;

    public string? FilePath { get; set; }

    public string? ProjectName { get; set; }

    public IReadOnlyList<string> ChangedFiles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> IncludePathPatterns { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludePathPatterns { get; set; } = Array.Empty<string>();

    public string? DiagnosticId { get; set; }

    public CodeDiagnosticSeverity? MinimumSeverity { get; set; } = CodeDiagnosticSeverity.Warning;

    public CodeDiagnosticNoiseProfile NoiseProfile { get; set; } = CodeDiagnosticNoiseProfile.Auto;

    public int MaxResults { get; set; } = 100;
}

public sealed class CSharpCodeFixCandidate
{
    public string DiagnosticId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public string Severity { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool SupportsPreview { get; set; }

    public bool SupportsApply { get; set; }

    public bool SupportsFixAll { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string EquivalenceKey { get; set; } = string.Empty;

    public string StableKey { get; set; } = string.Empty;

    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}

public sealed class CSharpCodeFixRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public CSharpMutationScopeKind ScopeKind { get; set; } = CSharpMutationScopeKind.Document;

    public string? FilePath { get; set; }

    public string? ProjectName { get; set; }

    public string DiagnosticId { get; set; } = string.Empty;

    public string? FixTitle { get; set; }

    public string? ProviderName { get; set; }

    public string? EquivalenceKey { get; set; }

    public string? CandidateStableKey { get; set; }

    public string? PreviewSessionId { get; set; }

    public string? ExpectedWorkspaceVersion { get; set; }

    public int MaxTextChanges { get; set; } = 1000;

    public int MaxSnippetLength { get; set; } = 200;

    public bool AllowSolutionScope { get; set; }
}

public sealed class CSharpCodeFixPreview
{
    public string DiagnosticId { get; set; } = string.Empty;

    public string FixTitle { get; set; } = string.Empty;

    public CSharpMutationScopeKind ScopeKind { get; set; }

    public WorkspaceMutationPreview MutationPreview { get; set; } = new();
}

public sealed class CSharpCodeFixApplyResult
{
    public bool Applied { get; set; }

    public CSharpCodeFixPreview Preview { get; set; } = new();

    public string ApplyFailure { get; set; } = string.Empty;

    public WorkspaceMutationApplyResult MutationResult { get; set; } = new();
}

public sealed class CSharpRefactoringPlanRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public CSharpRefactoringPlanKind Kind { get; set; } = CSharpRefactoringPlanKind.Unknown;

    public string? SymbolKey { get; set; }

    public SourceSpan? Position { get; set; }

    public string? FilePath { get; set; }

    public string? NewName { get; set; }

    public int MaxRelatedItems { get; set; } = 20;

    public bool IncludeGeneratedCode { get; set; }
}

public sealed class CSharpRefactoringPlan
{
    public CSharpRefactoringPlanKind Kind { get; set; }

    public string Summary { get; set; } = string.Empty;

    public bool IsSupportedForPreview { get; set; }

    public bool IsSupportedForApply { get; set; }

    public WorkspaceMutationKind MutationKind { get; set; } = WorkspaceMutationKind.Refactoring;

    public SymbolDescriptor? TargetSymbol { get; set; }

    public SourceSpan? TargetSpan { get; set; }

    public IReadOnlyList<string> RequiredInputs { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> SafetyChecks { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> RecommendedTools { get; set; } = Array.Empty<string>();

    public IReadOnlyList<CSharpRefactoringPlanStep> ExecutionSteps { get; set; } = Array.Empty<CSharpRefactoringPlanStep>();

    public IReadOnlyList<WorkspaceMutationBlocker> Blockers { get; set; } = Array.Empty<WorkspaceMutationBlocker>();
}

public sealed class CSharpRefactoringPlanStep
{
    public int Order { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string ArgumentsSummary { get; set; } = string.Empty;

    public bool IsMutating { get; set; }

    public bool RequiresUserApproval { get; set; }

    public string StopIf { get; set; } = string.Empty;
}
