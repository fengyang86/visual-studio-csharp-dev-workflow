using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class BuildDiagnosticsTools
{
    private readonly CodeNavigationTools _inner;

    public BuildDiagnosticsTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "analyze_csharp_build_errors", ReadOnly = true, Idempotent = true)]
    [Description("Parse C# build output and return ranked build issues, suppressing unrelated path noise and likely cascade errors.")]
    public Task<WorkspaceQueryResult<BuildTriageReport>> AnalyzeCSharpBuildErrors(
        [Description("Raw dotnet/MSBuild/Visual Studio build output text.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional file, directory, or wildcard patterns to include. Use this to focus on touched folders.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude. Use this to suppress known noisy legacy/generated folders.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. Issues in these files are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Maximum ranked build issues to return.")]
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        return _inner.AnalyzeCSharpBuildErrors(
            buildOutput,
            buildLogFilePath,
            includePathPatterns,
            excludePathPatterns,
            changedFiles,
            maxResults,
            cancellationToken);
    }

    [McpServerTool(Name = "get_csharp_diagnostics", ReadOnly = true, Idempotent = true)]
    [Description("Return compiler and analyzer diagnostics from the active Visual Studio C# workspace, optionally scoped to a project or document.")]
    public Task<WorkspaceQueryResult<CodeDiagnostic>> GetCSharpDiagnostics(
        [Description("Optional absolute source file path. When omitted, diagnostics are collected from matching projects or the whole solution.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include. Use this to focus diagnostics on changed files or touched folders.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude. Use this to suppress known noisy generated or legacy folders.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. Matching diagnostics are ranked higher.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name.")]
        string? projectName = null,
        [Description("Optional minimum severity filter: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = null,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off. Auto filters known noise only when no focused diagnostics scope is provided.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum number of diagnostics to return.")]
        int maxResults = 500,
        [Description("Maximum number of C# projects to process before returning a partial result. Use 0 for no project-count cap.")]
        int maxProjects = 0,
        [Description("Maximum elapsed milliseconds for diagnostics collection before returning a partial result. Defaults below the bridge timeout so partial results are preserved.")]
        int maxElapsedMilliseconds = 45000,
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
        return _inner.GetCSharpDiagnostics(
            filePath,
            includePathPatterns,
            excludePathPatterns,
            changedFiles,
            projectName,
            minimumSeverity,
            noiseProfile,
            maxResults,
            maxProjects,
            maxElapsedMilliseconds,
            includeGeneratedCode,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_studio_error_list", ReadOnly = true, Idempotent = true)]
    [Description("Return the current Visual Studio Error List items through EnvDTE. This is VS UI context, not a replacement for build output or Roslyn diagnostics.")]
    public Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetVisualStudioErrorList(
        [Description("Maximum number of Error List items to return.")]
        int maxResults = 100,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetVisualStudioErrorList(
            maxResults,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_studio_output_window", ReadOnly = true, Idempotent = true)]
    [Description("Return the tail text from a Visual Studio Output Window pane, usually Build. This is VS UI context and does not run a build.")]
    public Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetVisualStudioOutputWindow(
        [Description("Output Window pane name. Defaults to Build.")]
        string paneName = "Build",
        [Description("Maximum number of trailing characters to return.")]
        int maxCharacters = 20000,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetVisualStudioOutputWindow(
            paneName,
            maxCharacters,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }
}
