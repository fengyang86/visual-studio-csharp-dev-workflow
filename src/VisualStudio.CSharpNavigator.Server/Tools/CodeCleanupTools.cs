using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class CodeCleanupTools
{
    private readonly IVisualStudioWorkspaceBridge _workspaceBridge;

    public CodeCleanupTools(IVisualStudioWorkspaceBridge workspaceBridge)
    {
        _workspaceBridge = workspaceBridge;
    }

    [McpServerTool(Name = "preview_csharp_cleanup", ReadOnly = true, Idempotent = true)]
    [Description("Preview C# cleanup changes using Roslyn formatting, using organization, and simplification without applying edits.")]
    public Task<WorkspaceQueryResult<CSharpCleanupPreview>> PreviewCSharpCleanup(
        [Description("Cleanup scope: Document, Project, ChangedFiles, or Solution. Solution requires allowSolutionScope=true.")]
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document,
        [Description("Absolute source file path for document scope.")]
        string? filePath = null,
        [Description("Visual Studio project name for project scope.")]
        string? projectName = null,
        [Description("Changed file paths for changed-files scope.")]
        string[]? changedFiles = null,
        [Description("Cleanup operations to run. Empty means Format, OrganizeUsings, and Simplify.")]
        CSharpCleanupOperation[]? operations = null,
        [Description("Maximum number of text changes to include in the preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Allow solution-wide cleanup preview.")]
        bool allowSolutionScope = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCleanup(scopeKind, filePath, projectName, changedFiles, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCleanupPreview>(validation));
        }

        return _workspaceBridge.PreviewCleanupAsync(
            new CSharpCleanupRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ScopeKind = scopeKind,
                FilePath = filePath,
                ProjectName = projectName,
                ChangedFiles = Normalize(changedFiles),
                Operations = NormalizeOperations(operations),
                MaxTextChanges = maxTextChanges,
                MaxSnippetLength = maxSnippetLength,
                IncludeGeneratedCode = includeGeneratedCode,
                AllowSolutionScope = allowSolutionScope,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "apply_csharp_cleanup", ReadOnly = false, Idempotent = false)]
    [Description("Apply C# cleanup changes through VisualStudioWorkspace.TryApplyChanges after safety checks. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<CSharpCleanupApplyResult>> ApplyCSharpCleanup(
        [Description("Cleanup scope: Document, Project, ChangedFiles, or Solution. Solution requires allowSolutionScope=true.")]
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document,
        [Description("Absolute source file path for document scope.")]
        string? filePath = null,
        [Description("Visual Studio project name for project scope.")]
        string? projectName = null,
        [Description("Changed file paths for changed-files scope.")]
        string[]? changedFiles = null,
        [Description("Cleanup operations to run. Empty means Format, OrganizeUsings, and Simplify.")]
        CSharpCleanupOperation[]? operations = null,
        [Description("Maximum number of text changes to include in the returned preview.")]
        int maxTextChanges = 1000,
        [Description("Maximum old/new snippet length per text change.")]
        int maxSnippetLength = 200,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Allow solution-wide cleanup.")]
        bool allowSolutionScope = false,
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
        var targetValidation = ValidateExplicitTarget(targetPipeName, targetInstanceId, targetSolutionPath, "apply_csharp_cleanup");
        if (targetValidation is not null)
        {
            return Task.FromResult(Failure<CSharpCleanupApplyResult>(targetValidation));
        }

        var validation = ValidateCleanup(scopeKind, filePath, projectName, changedFiles, maxTextChanges, maxSnippetLength, allowSolutionScope);
        if (validation is not null)
        {
            return Task.FromResult(Failure<CSharpCleanupApplyResult>(validation));
        }

        return _workspaceBridge.ApplyCleanupAsync(
            new CSharpCleanupApplyRequest
            {
                Target = CreateTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ScopeKind = scopeKind,
                FilePath = filePath,
                ProjectName = projectName,
                ChangedFiles = Normalize(changedFiles),
                Operations = NormalizeOperations(operations),
                MaxTextChanges = maxTextChanges,
                MaxSnippetLength = maxSnippetLength,
                IncludeGeneratedCode = includeGeneratedCode,
                AllowSolutionScope = allowSolutionScope,
                AllowGeneratedDocumentChanges = allowGeneratedDocumentChanges,
                AllowUnsupportedDocumentChanges = allowUnsupportedDocumentChanges,
                AllowTruncatedPreview = allowTruncatedPreview,
            },
            cancellationToken);
    }

    private static string? ValidateCleanup(
        CSharpMutationScopeKind scopeKind,
        string? filePath,
        string? projectName,
        string[]? changedFiles,
        int maxTextChanges,
        int maxSnippetLength,
        bool allowSolutionScope)
    {
        if (maxTextChanges is < 1 or > 10000)
        {
            return "MaxTextChanges must be between 1 and 10000.";
        }

        if (maxSnippetLength is < 0 or > 2000)
        {
            return "MaxSnippetLength must be between 0 and 2000.";
        }

        if (scopeKind == CSharpMutationScopeKind.Solution && !allowSolutionScope)
        {
            return "SolutionScopeBlocked: solution-wide cleanup requires allowSolutionScope=true.";
        }

        return scopeKind switch
        {
            CSharpMutationScopeKind.Document when string.IsNullOrWhiteSpace(filePath) => "filePath is required for document cleanup scope.",
            CSharpMutationScopeKind.Project when string.IsNullOrWhiteSpace(projectName) => "projectName is required for project cleanup scope.",
            CSharpMutationScopeKind.ChangedFiles when Normalize(changedFiles).Length == 0 => "changedFiles is required for changed-files cleanup scope.",
            CSharpMutationScopeKind.Unknown => "scopeKind is required.",
            _ => null,
        };
    }

    private static string? ValidateExplicitTarget(string? targetPipeName, string? targetInstanceId, string? targetSolutionPath, string toolName)
    {
        return string.IsNullOrWhiteSpace(targetPipeName)
            && string.IsNullOrWhiteSpace(targetInstanceId)
            && string.IsNullOrWhiteSpace(targetSolutionPath)
                ? $"{toolName} requires an explicit targetPipeName, targetInstanceId, or targetSolutionPath."
                : null;
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

    private static string[] Normalize(string[]? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }

    private static CSharpCleanupOperation[] NormalizeOperations(CSharpCleanupOperation[]? operations)
    {
        return operations?
            .Where(operation => Enum.IsDefined(typeof(CSharpCleanupOperation), operation))
            .Distinct()
            .ToArray() ?? Array.Empty<CSharpCleanupOperation>();
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
