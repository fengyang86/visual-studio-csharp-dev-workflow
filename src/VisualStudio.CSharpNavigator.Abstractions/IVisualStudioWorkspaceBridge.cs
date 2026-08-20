using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Abstractions;

public interface IVisualStudioWorkspaceBridge
{
    Task<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>> ListVisualStudioInstancesAsync(
        VisualStudioInstancesRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<WorkspaceStatus>> GetWorkspaceStatusAsync(
        WorkspaceStatusRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolDescriptor>> SearchSymbolsAsync(
        SymbolSearchRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolReference>> FindReferencesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolDescriptor>> FindDefinitionsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolReference>> FindImplementationsAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolReference>> FindOverridesAsync(
        SymbolReferenceRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolDescription>> DescribeSymbolAsync(
        SymbolDescriptionRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SourceContextSnippet>> GetSymbolSourceAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextAsync(
        SourceContextRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DocumentSymbolNode>> ListDocumentSymbolsAsync(
        DocumentSymbolsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetOpenDocumentsAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetActiveDocumentContextAsync(
        VisualStudioDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SourceNavigationResult>> OpenSourceLocationAsync(
        SourceNavigationRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CodeDiagnostic>> GetDiagnosticsAsync(
        DiagnosticsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetErrorListAsync(
        ErrorListRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetOutputWindowAsync(
        OutputWindowRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CallGraphEdge>> FindCallersAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CallGraphEdge>> FindCalleesAsync(
        CallGraphRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeSymbolImpactAsync(
        SymbolImpactRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindDerivedTypesAsync(
        DerivedTypesRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<InheritanceChain>> GetInheritanceChainAsync(
        InheritanceChainRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<ProjectGraph>> GetProjectGraphAsync(
        ProjectGraphRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatusAsync(
        DebuggerStatusRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStackAsync(
        DebugCallStackRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugVariableInfo>> GetDebugStackFrameVariablesAsync(
        DebugStackFrameVariablesRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateDebugExpressionAsync(
        DebugExpressionRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreadsAsync(
        DebugThreadsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListDebugBreakpointsAsync(
        DebugBreakpointsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> StartDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> BreakDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> StopDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> StepOverAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> StepIntoAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> StepOutAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListGeneratedDocumentsAsync(
        GeneratedDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<TemporaryMarker>> FindTemporaryMarkersAsync(
        TemporaryMarkersRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<EnclosingContext>> GetEnclosingContextAsync(
        EnclosingContextRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindRelatedTestsAsync(
        RelatedTestsRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<RenamePreview>> PreviewRenameAsync(
        RenamePreviewRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<RenameApplyResult>> ApplyRenameAsync(
        RenameApplyRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCleanupPreview>> PreviewCleanupAsync(
        CSharpCleanupRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCleanupApplyResult>> ApplyCleanupAsync(
        CSharpCleanupApplyRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCodeFixCandidate>> ListCodeFixesAsync(
        CSharpCodeFixListRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCodeFixAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyFixAllAsync(
        CSharpCodeFixRequest request,
        CancellationToken cancellationToken);

    Task<WorkspaceQueryResult<CSharpRefactoringPlan>> PreviewRefactoringPlanAsync(
        CSharpRefactoringPlanRequest request,
        CancellationToken cancellationToken);
}
