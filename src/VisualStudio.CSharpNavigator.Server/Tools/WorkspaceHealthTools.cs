using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class WorkspaceHealthTools
{
    private readonly CodeNavigationTools _inner;

    public WorkspaceHealthTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "get_csharp_workspace_status", ReadOnly = true, Idempotent = true)]
    [Description("Return the active Visual Studio C# workspace status through the local VSIX bridge.")]
    public Task<WorkspaceQueryResult<WorkspaceStatus>> GetCSharpWorkspaceStatus(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetCSharpWorkspaceStatus(targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "check_visual_studio_csharp_navigator_health", ReadOnly = true, Idempotent = true)]
    [Description("Run a read-only health check for the Visual Studio C# Navigator bridge, workspace target selection, and optional scoped diagnostics preview.")]
    public Task<WorkspaceQueryResult<NavigatorHealthReport>> CheckVisualStudioCSharpNavigatorHealth(
        [Description("Whether to include a small Roslyn diagnostics preview after workspace selection succeeds.")]
        bool includeDiagnosticsPreview = false,
        [Description("Optional diagnostics include patterns used only when includeDiagnosticsPreview is true.")]
        string[]? includePathPatterns = null,
        [Description("Optional diagnostics exclude patterns used only when includeDiagnosticsPreview is true.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files used only when includeDiagnosticsPreview is true. Matching diagnostics are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name for diagnostics preview.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics preview: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled in the diagnostics preview: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum diagnostics preview items to return.")]
        int maxDiagnosticsPreview = 50,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.CheckVisualStudioCSharpNavigatorHealth(
            includeDiagnosticsPreview,
            includePathPatterns,
            excludePathPatterns,
            changedFiles,
            projectName,
            minimumSeverity,
            noiseProfile,
            maxDiagnosticsPreview,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }
}
