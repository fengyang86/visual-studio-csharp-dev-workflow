using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class RefactoringTools
{
    private readonly CodeNavigationTools _inner;
    private readonly VisualStudio.CSharpNavigator.Abstractions.IVisualStudioWorkspaceBridge _workspaceBridge;

    public RefactoringTools(
        CodeNavigationTools inner,
        VisualStudio.CSharpNavigator.Abstractions.IVisualStudioWorkspaceBridge workspaceBridge)
    {
        _inner = inner;
        _workspaceBridge = workspaceBridge;
    }

    [McpServerTool(Name = "preview_csharp_rename", ReadOnly = true, Idempotent = true)]
    [Description("Preview Roslyn C# symbol rename changes without applying edits to the workspace or files.")]
    public Task<WorkspaceQueryResult<RenamePreview>> PreviewCSharpRename(
        [Description("New C# identifier name for the target symbol.")]
        string newName,
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether Roslyn should rename overloads when supported for the symbol.")]
        bool renameOverloads = false,
        [Description("Whether Roslyn should rename matching text inside string literals.")]
        bool renameInStrings = false,
        [Description("Whether Roslyn should rename matching text inside comments.")]
        bool renameInComments = false,
        [Description("Whether Roslyn should include file rename metadata when supported. The MCP tool still only previews and never applies changes.")]
        bool renameFile = false,
        [Description("Maximum number of text changes to include in the preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.PreviewCSharpRename(
            newName,
            symbolKey,
            filePath,
            line,
            column,
            renameOverloads,
            renameInStrings,
            renameInComments,
            renameFile,
            maxTextChanges,
            maxSnippetLength,
            includeGeneratedCode,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "apply_csharp_rename", ReadOnly = false, Idempotent = false)]
    [Description("Apply a Roslyn C# symbol rename to the active Visual Studio workspace after safety checks. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<RenameApplyResult>> ApplyCSharpRename(
        [Description("New C# identifier name for the target symbol.")]
        string newName,
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Whether Roslyn should rename overloads when supported for the symbol.")]
        bool renameOverloads = false,
        [Description("Whether Roslyn should rename matching text inside string literals.")]
        bool renameInStrings = false,
        [Description("Whether Roslyn should rename matching text inside comments.")]
        bool renameInComments = false,
        [Description("Whether Roslyn should include file rename metadata when supported.")]
        bool renameFile = false,
        [Description("Maximum number of text changes to include in the returned preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Allow applying even when Roslyn reports rename conflicts.")]
        bool allowConflicts = false,
        [Description("Allow applying when generated document changes are included or omitted intentionally.")]
        bool allowGeneratedDocumentChanges = false,
        [Description("Allow applying when Roslyn produced added/removed document changes not represented as text diffs.")]
        bool allowUnsupportedDocumentChanges = false,
        [Description("Allow applying even when the returned preview was truncated by maxTextChanges.")]
        bool allowTruncatedPreview = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.ApplyCSharpRename(
            newName,
            symbolKey,
            filePath,
            line,
            column,
            renameOverloads,
            renameInStrings,
            renameInComments,
            renameFile,
            maxTextChanges,
            maxSnippetLength,
            includeGeneratedCode,
            allowConflicts,
            allowGeneratedDocumentChanges,
            allowUnsupportedDocumentChanges,
            allowTruncatedPreview,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "preview_csharp_refactoring_plan", ReadOnly = true, Idempotent = true)]
    [Description("Create a plan-only preview for complex C# refactorings, including supported tools, required inputs, blockers, and safety checks.")]
    public async Task<WorkspaceQueryResult<CSharpRefactoringPlan>> PreviewCSharpRefactoringPlan(
        [Description("Refactoring kind to plan, such as Rename, Cleanup, ExtractMethod, ChangeSignature, MoveType, or CodeFix.")]
        CSharpRefactoringPlanKind kind,
        [Description("Stable symbol key returned from a previous symbol query. Optional when source position is provided.")]
        string? symbolKey = null,
        [Description("Source file path for position-based lookup. Optional when symbol key is provided.")]
        string? filePath = null,
        [Description("One-based source line for position-based lookup.")]
        int? line = null,
        [Description("One-based source column for position-based lookup.")]
        int? column = null,
        [Description("Optional new name for rename-like planning.")]
        string? newName = null,
        [Description("Maximum related items to inspect for planning.")]
        int maxRelatedItems = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxRelatedItems is < 0 or > 500)
        {
            return Failure<CSharpRefactoringPlan>("maxRelatedItems must be between 0 and 500.");
        }

        if (kind == CSharpRefactoringPlanKind.Unknown)
        {
            return Failure<CSharpRefactoringPlan>("kind is required.");
        }

        var request = new CSharpRefactoringPlanRequest
        {
            Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            Kind = kind,
            SymbolKey = string.IsNullOrWhiteSpace(symbolKey) ? null : symbolKey,
            Position = CreatePosition(filePath, line, column),
            FilePath = filePath,
            NewName = newName,
            MaxRelatedItems = maxRelatedItems,
            IncludeGeneratedCode = includeGeneratedCode,
        };

        var result = await _workspaceBridge.PreviewRefactoringPlanAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var plan in result.Items)
        {
            EnrichRefactoringPlan(plan, request);
        }

        return result;
    }

    private static void EnrichRefactoringPlan(CSharpRefactoringPlan plan, CSharpRefactoringPlanRequest request)
    {
        plan.ExecutionSteps = CreateRefactoringExecutionSteps(request).ToArray();
        plan.Blockers = plan.Blockers
            .Concat(CreateRefactoringInputBlockers(request))
            .GroupBy(blocker => blocker.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IEnumerable<CSharpRefactoringPlanStep> CreateRefactoringExecutionSteps(CSharpRefactoringPlanRequest request)
    {
        return request.Kind switch
        {
            CSharpRefactoringPlanKind.Rename => CreateRenameExecutionSteps(),
            CSharpRefactoringPlanKind.Cleanup => CreateCleanupExecutionSteps(),
            CSharpRefactoringPlanKind.CodeFix => CreateCodeFixExecutionSteps(),
            CSharpRefactoringPlanKind.ExtractMethod => CreatePlanOnlyExecutionSteps(
                "Select the exact source span to extract and inspect the enclosing member before manual or future provider-backed refactoring.",
                "filePath, line/column range or editor selection; target method name; changedFiles for verification"),
            CSharpRefactoringPlanKind.ChangeSignature => CreatePlanOnlyExecutionSteps(
                "Inspect callers and overrides before changing parameters or return shape.",
                "symbolKey or source position; desired parameter order/defaults; changedFiles for verification"),
            CSharpRefactoringPlanKind.MoveType => CreatePlanOnlyExecutionSteps(
                "Inspect type declaration, namespace policy, project boundaries, and references before moving files.",
                "symbolKey or type position; destination project/file path; namespace policy; changedFiles for verification"),
            _ => Array.Empty<CSharpRefactoringPlanStep>(),
        };
    }

    private static CSharpRefactoringPlanStep[] CreateRenameExecutionSteps()
    {
        return new[]
        {
            CreateStep(
                1,
                "preview_csharp_rename",
                "Preview the exact Roslyn rename diff.",
                "symbolKey or filePath/line/column, newName, explicit target.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop on conflicts, truncated preview, generated document omissions, or unsupported document changes."),
            CreateStep(
                2,
                "apply_csharp_rename",
                "Apply the approved Roslyn rename through the workspace mutation pipeline.",
                "same target and newName; allow* flags only when the preview risk was explicitly accepted.",
                isMutating: true,
                requiresUserApproval: true,
                stopIf: "Stop if preview was not reviewed, target is ambiguous, workspace changed, or safety blockers remain."),
            CreateStep(
                3,
                "plan_csharp_verification",
                "Generate the smallest post-rename build/test plan.",
                "changedFiles from the mutation preview plus symbol/project scope.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if no focused verification command can be produced; ask for changedFiles or projectName."),
        };
    }

    private static CSharpRefactoringPlanStep[] CreateCleanupExecutionSteps()
    {
        return new[]
        {
            CreateStep(
                1,
                "preview_csharp_cleanup",
                "Preview formatting, using cleanup, or simplification changes in a bounded scope.",
                "document, changed-files, project, or explicitly allowed solution scope.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop on truncated preview, generated document omissions, unsupported document changes, or ambiguous scope."),
            CreateStep(
                2,
                "apply_csharp_cleanup",
                "Apply the approved cleanup preview through the workspace mutation pipeline.",
                "same scope and explicit target; allow* flags only when the preview risk was explicitly accepted.",
                isMutating: true,
                requiresUserApproval: true,
                stopIf: "Stop if preview was not reviewed, target is ambiguous, workspace changed, or safety blockers remain."),
            CreateStep(
                3,
                "plan_csharp_verification",
                "Generate focused verification for the cleaned files.",
                "changedFiles from the cleanup preview plus project scope.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if cleanup scope is too broad for focused verification."),
        };
    }

    private static CSharpRefactoringPlanStep[] CreateCodeFixExecutionSteps()
    {
        return new[]
        {
            CreateStep(
                1,
                "list_csharp_code_fixes",
                "List provider-backed CodeFix candidates in a scoped diagnostic slice.",
                "diagnosticId plus file/project/changed-files scope, include/exclude patterns, noiseProfile.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if candidate identity is missing, ambiguous, generated-only, or outside the requested scope."),
            CreateStep(
                2,
                "preview_csharp_code_fix or preview_csharp_fix_all",
                "Preview the exact CodeAction or FixAll mutation and create a replayable session.",
                "candidate identity from list_csharp_code_fixes, diagnosticId, scope, explicit target.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop on blockers, truncated preview, conflicts, unsupported documents, or generated omissions."),
            CreateStep(
                3,
                "apply_csharp_code_fix or apply_csharp_fix_all",
                "Replay the preview session and apply only the approved mutation.",
                "previewSessionId plus matching candidate identity and explicit target.",
                isMutating: true,
                requiresUserApproval: true,
                stopIf: "Stop if previewSessionId is absent, stale, mismatched, or safety blockers remain."),
            CreateStep(
                4,
                "plan_csharp_verification",
                "Generate focused verification for the affected documents/projects.",
                "changedFiles from the mutation preview plus diagnostic/project scope.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if no focused verification command can be produced."),
        };
    }

    private static CSharpRefactoringPlanStep[] CreatePlanOnlyExecutionSteps(string purpose, string argumentsSummary)
    {
        return new[]
        {
            CreateStep(
                1,
                "get_csharp_source_context",
                purpose,
                argumentsSummary,
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if the source context is truncated or does not include the intended target span."),
            CreateStep(
                2,
                "analyze_csharp_symbol_impact",
                "Inspect references, callers, callees, overrides, implementations, and cross-project impact before editing.",
                "symbolKey from source context or symbol search; maxRelatedItems tuned to scope.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if symbol resolution is ambiguous or impact is broader than the requested scope."),
            CreateStep(
                3,
                "normal_source_edit",
                "Perform the refactor as explicit source edits because provider-backed apply is not implemented for this kind yet.",
                "bounded source snippets and impact evidence from previous steps.",
                isMutating: true,
                requiresUserApproval: false,
                stopIf: "Stop if the edit would require semantic behavior not proven by source context and impact evidence."),
            CreateStep(
                4,
                "plan_csharp_verification",
                "Generate focused verification after the manual refactor.",
                "changedFiles, symbolQuery, projectName, and any build output.",
                isMutating: false,
                requiresUserApproval: false,
                stopIf: "Stop if no focused verification command can be produced."),
        };
    }

    private static IEnumerable<WorkspaceMutationBlocker> CreateRefactoringInputBlockers(CSharpRefactoringPlanRequest request)
    {
        var hasSymbolTarget = !string.IsNullOrWhiteSpace(request.SymbolKey) || request.Position is not null;
        if (request.Kind is CSharpRefactoringPlanKind.Rename or CSharpRefactoringPlanKind.ExtractMethod or CSharpRefactoringPlanKind.ChangeSignature or CSharpRefactoringPlanKind.MoveType
            && !hasSymbolTarget)
        {
            yield return CreateBlocker("MissingSymbolTarget", "Provide symbolKey or filePath/line/column before previewing this refactoring route.");
        }

        if (request.Kind == CSharpRefactoringPlanKind.Rename && string.IsNullOrWhiteSpace(request.NewName))
        {
            yield return CreateBlocker("MissingNewName", "Provide newName before calling preview_csharp_rename.");
        }

        if (request.Kind == CSharpRefactoringPlanKind.CodeFix)
        {
            yield return CreateBlocker("MissingDiagnosticScope", "Provide diagnosticId and a focused file/project/changed-files scope when listing or previewing CodeFix candidates.");
        }
    }

    private static CSharpRefactoringPlanStep CreateStep(
        int order,
        string toolName,
        string purpose,
        string argumentsSummary,
        bool isMutating,
        bool requiresUserApproval,
        string stopIf)
    {
        return new CSharpRefactoringPlanStep
        {
            Order = order,
            ToolName = toolName,
            Purpose = purpose,
            ArgumentsSummary = argumentsSummary,
            IsMutating = isMutating,
            RequiresUserApproval = requiresUserApproval,
            StopIf = stopIf,
        };
    }

    private static WorkspaceMutationBlocker CreateBlocker(string code, string message)
    {
        return new WorkspaceMutationBlocker
        {
            Kind = WorkspaceMutationBlockerKind.InvalidScope,
            Code = code,
            Message = message,
        };
    }

    private static SourceSpan? CreatePosition(string? filePath, int? line, int? column)
    {
        if (string.IsNullOrWhiteSpace(filePath) || line is null || column is null)
        {
            return null;
        }

        return new SourceSpan
        {
            FilePath = filePath,
            StartLine = line.Value,
            StartColumn = column.Value,
            EndLine = line.Value,
            EndColumn = column.Value,
        };
    }

    private static VisualStudioBridgeTarget CreateTarget(string? pipeName, string? instanceId, string? solutionPath)
    {
        return new VisualStudioBridgeTarget
        {
            PipeName = string.IsNullOrWhiteSpace(pipeName) ? string.Empty : pipeName,
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId,
            SolutionPath = string.IsNullOrWhiteSpace(solutionPath) ? string.Empty : solutionPath,
        };
    }

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }
}
