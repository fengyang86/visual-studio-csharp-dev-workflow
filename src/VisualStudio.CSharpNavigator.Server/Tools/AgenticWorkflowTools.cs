using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class AgenticWorkflowTools
{
    private readonly WorkflowKernel _workflowKernel;

    public AgenticWorkflowTools(WorkflowKernel workflowKernel)
    {
        _workflowKernel = workflowKernel;
    }

    [McpServerTool(Name = "prepare_csharp_edit_task", ReadOnly = true, Idempotent = true)]
    [Description("Prepare a read-only Agentic C# edit task packet with evidence, candidate edit locations, bounded source context, next actions, and safety blockers.")]
    public Task<WorkspaceQueryResult<CSharpEditTaskResult>> PrepareCSharpEditTask(
        [Description("Short problem statement or task context for the edit task.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Optional symbol name or partial name to search after workspace health succeeds.")]
        string? symbolQuery = null,
        [Description("Optional absolute source file path used to scope diagnostics and source snippets.")]
        string? filePath = null,
        [Description("Optional file, directory, or wildcard patterns to include for build triage and diagnostics.")]
        string[]? includePathPatterns = null,
        [Description("Optional file, directory, or wildcard patterns to exclude for build triage and diagnostics.")]
        string[]? excludePathPatterns = null,
        [Description("Optional changed files from git diff or the current task. These focus build triage, diagnostics, and source snippets.")]
        string[]? changedFiles = null,
        [Description("Optional Visual Studio project name for scoped diagnostics.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Whether to query whole-solution diagnostics when no file/project/path scope is available.")]
        bool includeWholeSolutionDiagnostics = false,
        [Description("Whether to read the Visual Studio Output Window Build pane for build triage when buildOutput and buildLogFilePath are omitted.")]
        bool includeVisualStudioBuildOutput = true,
        [Description("Maximum trailing characters to read from the Visual Studio Output Window Build pane.")]
        int maxVisualStudioBuildOutputCharacters = 20000,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbol candidates to return.")]
        int maxSymbols = 20,
        [Description("Maximum references, callers, callees, and related tests to return for a unique symbol.")]
        int maxRelatedItems = 20,
        [Description("Maximum source snippets to include in the task context package.")]
        int maxSourceSnippets = 8,
        [Description("Number of source lines to include around each snippet.")]
        int contextLines = 3,
        [Description("Maximum characters returned per source snippet request.")]
        int maxCharsPerSnippet = 8000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Response detail level: Compact returns only the decision-ready summary, Standard returns bounded supporting evidence, and Full embeds the complete task package when within budget.")]
        WorkflowResponseDetailLevel detailLevel = WorkflowResponseDetailLevel.Compact,
        [Description("Optional workspace context lease returned by a previous task. Reuses its resolved bridge pipe and avoids repeated discovery.")]
        string? workspaceContextLeaseId = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workflowKernel.PrepareCSharpEditTaskAsync(
            new CSharpEditTaskRequest
            {
                ProblemText = problemText ?? string.Empty,
                BuildOutput = buildOutput,
                BuildLogFilePath = buildLogFilePath,
                SymbolQuery = symbolQuery,
                FilePath = filePath,
                IncludePathPatterns = includePathPatterns ?? Array.Empty<string>(),
                ExcludePathPatterns = excludePathPatterns ?? Array.Empty<string>(),
                ChangedFiles = changedFiles ?? Array.Empty<string>(),
                ProjectName = projectName,
                MinimumSeverity = minimumSeverity,
                NoiseProfile = noiseProfile,
                IncludeWholeSolutionDiagnostics = includeWholeSolutionDiagnostics,
                IncludeVisualStudioBuildOutput = includeVisualStudioBuildOutput,
                MaxVisualStudioBuildOutputCharacters = maxVisualStudioBuildOutputCharacters,
                MaxBuildIssues = maxBuildIssues,
                MaxDiagnostics = maxDiagnostics,
                MaxSymbols = maxSymbols,
                MaxRelatedItems = maxRelatedItems,
                MaxSourceSnippets = maxSourceSnippets,
                ContextLines = contextLines,
                MaxCharsPerSnippet = maxCharsPerSnippet,
                IncludeGeneratedCode = includeGeneratedCode,
                DetailLevel = detailLevel,
                WorkspaceContextLeaseId = workspaceContextLeaseId,
                Target = new VisualStudioBridgeTarget
                {
                    PipeName = targetPipeName ?? string.Empty,
                    InstanceId = targetInstanceId ?? string.Empty,
                    SolutionPath = targetSolutionPath ?? string.Empty,
                },
            },
            cancellationToken);
    }

    [McpServerTool(Name = "prepare_csharp_change_review", ReadOnly = true, Idempotent = true)]
    [Description("Prepare a read-only Agentic C# change review packet with risks, test gaps, candidate edit locations, evidence resources, and safety blockers.")]
    public Task<WorkspaceQueryResult<CSharpChangeReviewTaskResult>> PrepareCSharpChangeReview(
        [Description("Short problem statement or change summary for the review.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Changed C# files for current-task review focus.")]
        string[]? changedFiles = null,
        [Description("Optional area path patterns, directories, or files to review.")]
        string[]? areaPaths = null,
        [Description("Optional symbol name or partial name to review.")]
        string? symbolQuery = null,
        [Description("Optional Visual Studio project name for scoped diagnostics and marker scanning.")]
        string? projectName = null,
        [Description("Optional diagnostics include patterns.")]
        string[]? includePathPatterns = null,
        [Description("Optional diagnostics exclude patterns.")]
        string[]? excludePathPatterns = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum symbol candidates to return.")]
        int maxSymbols = 20,
        [Description("Maximum references, impact groups, related tests, and markers to return.")]
        int maxRelatedItems = 20,
        [Description("Maximum primary files and findings to return.")]
        int maxFiles = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Response detail level: Compact returns only the decision-ready summary, Standard returns bounded supporting evidence, and Full embeds the complete task package when within budget.")]
        WorkflowResponseDetailLevel detailLevel = WorkflowResponseDetailLevel.Compact,
        [Description("Optional workspace context lease returned by a previous task. Reuses its resolved bridge pipe and avoids repeated discovery.")]
        string? workspaceContextLeaseId = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workflowKernel.PrepareCSharpChangeReviewAsync(
            new CSharpChangeReviewTaskRequest
            {
                ProblemText = problemText ?? string.Empty,
                BuildOutput = buildOutput,
                BuildLogFilePath = buildLogFilePath,
                ChangedFiles = changedFiles ?? Array.Empty<string>(),
                AreaPaths = areaPaths ?? Array.Empty<string>(),
                SymbolQuery = symbolQuery,
                ProjectName = projectName,
                IncludePathPatterns = includePathPatterns ?? Array.Empty<string>(),
                ExcludePathPatterns = excludePathPatterns ?? Array.Empty<string>(),
                MinimumSeverity = minimumSeverity,
                NoiseProfile = noiseProfile,
                MaxBuildIssues = maxBuildIssues,
                MaxDiagnostics = maxDiagnostics,
                MaxSymbols = maxSymbols,
                MaxRelatedItems = maxRelatedItems,
                MaxFiles = maxFiles,
                IncludeGeneratedCode = includeGeneratedCode,
                DetailLevel = detailLevel,
                WorkspaceContextLeaseId = workspaceContextLeaseId,
                Target = new VisualStudioBridgeTarget
                {
                    PipeName = targetPipeName ?? string.Empty,
                    InstanceId = targetInstanceId ?? string.Empty,
                    SolutionPath = targetSolutionPath ?? string.Empty,
                },
            },
            cancellationToken);
    }

    [McpServerTool(Name = "prepare_csharp_verification_run", ReadOnly = true, Idempotent = true)]
    [Description("Prepare a read-only Agentic C# verification run packet with smoke, focused, and broad build/test command tiers plus evidence resources and safety blockers.")]
    public Task<WorkspaceQueryResult<CSharpVerificationRunTaskResult>> PrepareCSharpVerificationRun(
        [Description("Short problem statement or change summary for verification planning.")]
        string? problemText = null,
        [Description("Optional raw dotnet/MSBuild/Visual Studio build output text to triage.")]
        string? buildOutput = null,
        [Description("Optional path to a compact build log file. Used together with buildOutput when both are provided.")]
        string? buildLogFilePath = null,
        [Description("Changed C# files for current-task verification focus.")]
        string[]? changedFiles = null,
        [Description("Optional area path patterns, directories, or files to review with the verification plan.")]
        string[]? areaPaths = null,
        [Description("Optional symbol name or partial name to scope related tests and impact.")]
        string? symbolQuery = null,
        [Description("Optional absolute source file path used to scope diagnostics and project inference.")]
        string? filePath = null,
        [Description("Optional diagnostics include patterns.")]
        string[]? includePathPatterns = null,
        [Description("Optional diagnostics exclude patterns.")]
        string[]? excludePathPatterns = null,
        [Description("Optional Visual Studio project name for scoped diagnostics and project build suggestions.")]
        string? projectName = null,
        [Description("Optional minimum severity filter for diagnostics: Hidden, Info, Warning, or Error.")]
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        [Description("How known noisy diagnostics from generated/vendor/legacy paths are handled: Auto, Penalize, Filter, or Off.")]
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        [Description("Whether to read the Visual Studio Output Window Build pane for build triage when buildOutput and buildLogFilePath are omitted.")]
        bool includeVisualStudioBuildOutput = true,
        [Description("Maximum trailing characters to read from the Visual Studio Output Window Build pane.")]
        int maxVisualStudioBuildOutputCharacters = 20000,
        [Description("Maximum build issues to return from build-log triage.")]
        int maxBuildIssues = 20,
        [Description("Maximum Roslyn diagnostics to return.")]
        int maxDiagnostics = 30,
        [Description("Maximum related tests to return.")]
        int maxRelatedTests = 20,
        [Description("Maximum references, impact groups, and markers to return for review context.")]
        int maxRelatedItems = 20,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Response detail level: Compact returns only the decision-ready summary, Standard returns bounded supporting evidence, and Full embeds the complete task package when within budget.")]
        WorkflowResponseDetailLevel detailLevel = WorkflowResponseDetailLevel.Compact,
        [Description("Optional workspace context lease returned by a previous task. Reuses its resolved bridge pipe and avoids repeated discovery.")]
        string? workspaceContextLeaseId = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workflowKernel.PrepareCSharpVerificationRunAsync(
            new CSharpVerificationRunTaskRequest
            {
                ProblemText = problemText ?? string.Empty,
                BuildOutput = buildOutput,
                BuildLogFilePath = buildLogFilePath,
                ChangedFiles = changedFiles ?? Array.Empty<string>(),
                AreaPaths = areaPaths ?? Array.Empty<string>(),
                SymbolQuery = symbolQuery,
                FilePath = filePath,
                IncludePathPatterns = includePathPatterns ?? Array.Empty<string>(),
                ExcludePathPatterns = excludePathPatterns ?? Array.Empty<string>(),
                ProjectName = projectName,
                MinimumSeverity = minimumSeverity,
                NoiseProfile = noiseProfile,
                IncludeVisualStudioBuildOutput = includeVisualStudioBuildOutput,
                MaxVisualStudioBuildOutputCharacters = maxVisualStudioBuildOutputCharacters,
                MaxBuildIssues = maxBuildIssues,
                MaxDiagnostics = maxDiagnostics,
                MaxRelatedTests = maxRelatedTests,
                MaxRelatedItems = maxRelatedItems,
                IncludeGeneratedCode = includeGeneratedCode,
                DetailLevel = detailLevel,
                WorkspaceContextLeaseId = workspaceContextLeaseId,
                Target = new VisualStudioBridgeTarget
                {
                    PipeName = targetPipeName ?? string.Empty,
                    InstanceId = targetInstanceId ?? string.Empty,
                    SolutionPath = targetSolutionPath ?? string.Empty,
                },
            },
            cancellationToken);
    }

    [McpServerTool(Name = "investigate_csharp_runtime_exception", ReadOnly = true, Idempotent = true)]
    [Description("Prepare a read-only Agentic C# runtime exception packet with debugger status, call stack, source snippets, optional artifact evidence, next actions, and safety blockers.")]
    public Task<WorkspaceQueryResult<CSharpRuntimeExceptionTaskResult>> InvestigateCSharpRuntimeException(
        [Description("Short problem statement or runtime failure summary.")]
        string? problemText = null,
        [Description("Optional exception text, stack trace, or user-observed runtime failure text.")]
        string? exceptionText = null,
        [Description("Optional report, trace, log, JSON, XML/TRX, or text artifact paths to summarize with the debug evidence.")]
        string[]? artifactPaths = null,
        [Description("Optional text patterns to focus artifact summaries.")]
        string[]? includeTextPatterns = null,
        [Description("Maximum breakpoints to include in the debug evidence package.")]
        int maxBreakpoints = 100,
        [Description("Maximum call stack frames to include when the debugger is paused.")]
        int maxFrames = 50,
        [Description("Maximum source snippets to collect for stack frames.")]
        int maxSourceSnippets = 6,
        [Description("Number of source lines to include around each snippet.")]
        int contextLines = 3,
        [Description("Maximum characters returned per source snippet request.")]
        int maxCharsPerSnippet = 8000,
        [Description("Whether generated code should be included when the bridge supports it.")]
        bool includeGeneratedCode = false,
        [Description("Response detail level: Compact returns only the decision-ready summary, Standard returns bounded supporting evidence, and Full embeds the complete task package when within budget.")]
        WorkflowResponseDetailLevel detailLevel = WorkflowResponseDetailLevel.Compact,
        [Description("Optional workspace context lease returned by a previous task. Reuses its resolved bridge pipe and avoids repeated discovery.")]
        string? workspaceContextLeaseId = null,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _workflowKernel.InvestigateCSharpRuntimeExceptionAsync(
            new CSharpRuntimeExceptionTaskRequest
            {
                ProblemText = problemText ?? string.Empty,
                ExceptionText = exceptionText ?? string.Empty,
                ArtifactPaths = artifactPaths ?? Array.Empty<string>(),
                IncludeTextPatterns = includeTextPatterns ?? Array.Empty<string>(),
                MaxBreakpoints = maxBreakpoints,
                MaxFrames = maxFrames,
                MaxSourceSnippets = maxSourceSnippets,
                ContextLines = contextLines,
                MaxCharsPerSnippet = maxCharsPerSnippet,
                IncludeGeneratedCode = includeGeneratedCode,
                DetailLevel = detailLevel,
                WorkspaceContextLeaseId = workspaceContextLeaseId,
                Target = new VisualStudioBridgeTarget
                {
                    PipeName = targetPipeName ?? string.Empty,
                    InstanceId = targetInstanceId ?? string.Empty,
                    SolutionPath = targetSolutionPath ?? string.Empty,
                },
            },
            cancellationToken);
    }
}
