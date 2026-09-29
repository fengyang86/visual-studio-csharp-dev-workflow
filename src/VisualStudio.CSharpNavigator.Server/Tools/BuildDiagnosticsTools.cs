using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class BuildDiagnosticsTools
{
    private readonly CodeNavigationTools _inner;
    private readonly IVisualStudioWorkspaceBridge _workspaceBridge;

    public BuildDiagnosticsTools(CodeNavigationTools inner, IVisualStudioWorkspaceBridge workspaceBridge)
    {
        _inner = inner;
        _workspaceBridge = workspaceBridge;
    }

    [McpServerTool(Name = "get_csharp_live_diagnostics", ReadOnly = true, Idempotent = true)]
    [Description("Return the C# diagnostics Visual Studio has ALREADY computed through its live background analysis (compiler + analyzers) - near-instant on warm solutions and never triggers a fresh analyzer pass. Completeness is best-effort while background analysis is pending (right after opening a solution or after edits); for guaranteed complete synchronous analysis use get_csharp_diagnostics instead.")]
    public Task<WorkspaceQueryResult<CodeDiagnostic>> GetCSharpLiveDiagnostics(
        [Description("Optional absolute file path to scope to one document.")] string? filePath = null,
        [Description("Optional project name to scope the live diagnostics.")] string? projectName = null,
        [Description("Optional file, directory, or wildcard patterns to include.")] string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude.")] string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff; used only for relevance scoring.")] string[]? changedFiles = null,
        [Description("Optional minimum severity filter: Hidden, Info, Warning, or Error.")] CodeDiagnosticSeverity? minimumSeverity = null,
        [Description("Maximum diagnostics to return.")] int maxResults = 200,
        [Description("Include generated documents such as .Designer.cs and source-generated files.")] bool includeGeneratedCode = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")] string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id. Preferred for repeated calls in one session.")] string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path. Fails with candidates when ambiguous.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workspaceBridge.GetLiveDiagnosticsAsync(
            new LiveDiagnosticsRequest
            {
                Target = CreateLiveTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                IncludePathPatterns = includePathPatterns ?? Array.Empty<string>(),
                ExcludePathPatterns = excludePathPatterns ?? Array.Empty<string>(),
                ChangedFiles = changedFiles ?? Array.Empty<string>(),
                MinimumSeverity = minimumSeverity,
                MaxResults = maxResults,
                IncludeGeneratedCode = includeGeneratedCode,
            },
            cancellationToken);
    }

    private static VisualStudioBridgeTarget CreateLiveTarget(string? pipeName, string? instanceId, string? solutionPath)
    {
        return new VisualStudioBridgeTarget
        {
            PipeName = string.IsNullOrWhiteSpace(pipeName) ? string.Empty : pipeName,
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId,
            SolutionPath = string.IsNullOrWhiteSpace(solutionPath) ? string.Empty : solutionPath,
        };
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
    [Description("Return compiler and analyzer diagnostics from the active Visual Studio C# workspace, optionally scoped to a project or document. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
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
        [Description("Diagnostic coverage: Auto uses Fast for unscoped work and Complete for a focused scope; Fast favors latency and may stop early; Complete attempts all matching projects within the time budget.")]
        CodeDiagnosticCollectionMode collectionMode = CodeDiagnosticCollectionMode.Auto,
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
            collectionMode,
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
    [Description("Return the current Visual Studio Error List items through EnvDTE. This is VS UI context, not a replacement for build output or Roslyn diagnostics. Result shape: items[] (typed results), diagnostics[] (string notes), isPartial (bool, true when truncated), succeeded (bool, false only on rejection), errorCode (optional string, from the first diagnostic code prefix).")]
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

    [McpServerTool(Name = "start_visual_studio_build", ReadOnly = false, Idempotent = false)]
    [Description("Start a Visual Studio build (fire-and-forget) through the VS solution build system, using the user's active configuration and startup projects. Mutates Visual Studio state; requires an explicit target. Poll get_visual_studio_build_status for completion, then get_visual_studio_error_list for errors. Complements execute_csharp_verification (which runs dotnet in a shell).")]
    public Task<WorkspaceQueryResult<VisualStudioBuildResult>> StartVisualStudioBuild(
        [Description("Optional project name to build one project; omit for the full solution build.")] string? projectName = null,
        [Description("true cleans before building (rebuild semantics).")] bool rebuild = false,
        [Description("Target Visual Studio bridge pipe name.")] string? targetPipeName = null,
        [Description("Target Visual Studio bridge instance id.")] string? targetInstanceId = null,
        [Description("Target Visual Studio solution path.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPipeName) && string.IsNullOrWhiteSpace(targetInstanceId) && string.IsNullOrWhiteSpace(targetSolutionPath))
        {
            return Task.FromResult(new WorkspaceQueryResult<VisualStudioBuildResult>
            {
                Diagnostics = new[] { "start_visual_studio_build requires an explicit targetPipeName, targetInstanceId, or targetSolutionPath." },
                IsPartial = true,
                Succeeded = false,
            });
        }

        return _workspaceBridge.StartVisualStudioBuildAsync(
            new VisualStudioBuildRequest
            {
                Target = CreateLiveTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? string.Empty : projectName,
                Rebuild = rebuild,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_studio_build_status", ReadOnly = true, Idempotent = true)]
    [Description("Read the current Visual Studio solution build state: InProgress / Done / NotStarted, startup projects, and active configuration. Pair with get_visual_studio_error_list for actual error details after a build.")]
    public Task<WorkspaceQueryResult<VisualStudioBuildStatus>> GetVisualStudioBuildStatus(
        [Description("Target Visual Studio bridge pipe name.")] string? targetPipeName = null,
        [Description("Target Visual Studio bridge instance id.")] string? targetInstanceId = null,
        [Description("Target Visual Studio solution path.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workspaceBridge.GetVisualStudioBuildStatusAsync(
            new VisualStudioBuildStatusRequest
            {
                Target = CreateLiveTarget(targetPipeName, targetInstanceId, targetSolutionPath),
            },
            cancellationToken);
    }

    [McpServerTool(Name = "get_visual_studio_activity", ReadOnly = true, Idempotent = true)]
    [Description("Read recent in-Visual-Studio activity events (builds started/finished, debugger mode transitions, solution open/close, documents opened/saved) from a bounded ring buffer subscribed to EnvDTE events. Use this to learn what the HUMAN did in Visual Studio since you last looked, even when you were not the one driving.")]
    public Task<WorkspaceQueryResult<VisualStudioActivityResult>> GetVisualStudioActivity(
        [Description("Maximum events to return (1-200, newest first).")] int maxEvents = 50,
        [Description("Optional category filter: build, debug, solution, document, or system.")] string? category = null,
        [Description("Target Visual Studio bridge pipe name.")] string? targetPipeName = null,
        [Description("Target Visual Studio bridge instance id.")] string? targetInstanceId = null,
        [Description("Target Visual Studio solution path.")] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        if (maxEvents is < 1 or > 200)
        {
            return Task.FromResult(new WorkspaceQueryResult<VisualStudioActivityResult>
            {
                Diagnostics = new[] { "MaxEvents must be between 1 and 200." },
                IsPartial = true,
                Succeeded = false,
            });
        }

        return _workspaceBridge.GetVisualStudioActivityAsync(
            new VisualStudioActivityRequest
            {
                Target = CreateLiveTarget(targetPipeName, targetInstanceId, targetSolutionPath),
                MaxEvents = maxEvents,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
            },
            cancellationToken);
    }
}
