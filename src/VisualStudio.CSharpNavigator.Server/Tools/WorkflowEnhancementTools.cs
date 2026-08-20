using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class WorkflowEnhancementTools
{
    private readonly CodeNavigationTools _inner;

    public WorkflowEnhancementTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "investigate_csharp_build_failure", ReadOnly = true, Idempotent = true)]
    [Description("Create a compact build-failure context by combining build triage, VS Build pane, Error List, scoped diagnostics, source snippets, and next actions.")]
    public Task<WorkspaceQueryResult<CSharpBuildFailureContext>> InvestigateCSharpBuildFailure(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxErrorListItems = 50,
        int maxSourceSnippets = 8,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.InvestigateCSharpBuildFailure(problemText, buildOutput, buildLogFilePath, changedFiles, filePath, includePathPatterns, excludePathPatterns, projectName, minimumSeverity, noiseProfile, includeVisualStudioBuildOutput, maxVisualStudioBuildOutputCharacters, maxBuildIssues, maxDiagnostics, maxErrorListItems, maxSourceSnippets, contextLines, maxCharsPerSnippet, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "collect_artifact_evidence", ReadOnly = true, Idempotent = true)]
    [Description("Collect compact evidence from local report, trace, log, JSON, or text artifacts without executing the application.")]
    public Task<WorkspaceQueryResult<ArtifactEvidenceReport>> CollectArtifactEvidence(
        string[]? artifactPaths = null,
        string[]? rootDirectories = null,
        string[]? searchPatterns = null,
        string[]? includeTextPatterns = null,
        string[]? excludePathPatterns = null,
        int maxArtifacts = 20,
        int maxLinesPerArtifact = 20,
        int maxCharsPerArtifact = 12000,
        CancellationToken cancellationToken = default)
    {
        return _inner.CollectArtifactEvidence(artifactPaths, rootDirectories, searchPatterns, includeTextPatterns, excludePathPatterns, maxArtifacts, maxLinesPerArtifact, maxCharsPerArtifact, cancellationToken);
    }

    [McpServerTool(Name = "wait_for_artifact_evidence", ReadOnly = true, Idempotent = true)]
    [Description("Wait for local report, trace, log, JSON, or text artifacts to appear, then collect compact evidence.")]
    public Task<WorkspaceQueryResult<ArtifactEvidenceReport>> WaitForArtifactEvidence(
        string[]? artifactPaths = null,
        string[]? rootDirectories = null,
        string[]? searchPatterns = null,
        string[]? includeTextPatterns = null,
        string[]? excludePathPatterns = null,
        int timeoutMilliseconds = 30000,
        int pollIntervalMilliseconds = 1000,
        int maxArtifacts = 20,
        int maxLinesPerArtifact = 20,
        int maxCharsPerArtifact = 12000,
        CancellationToken cancellationToken = default)
    {
        return _inner.WaitForArtifactEvidence(artifactPaths, rootDirectories, searchPatterns, includeTextPatterns, excludePathPatterns, timeoutMilliseconds, pollIntervalMilliseconds, maxArtifacts, maxLinesPerArtifact, maxCharsPerArtifact, cancellationToken);
    }

    [McpServerTool(Name = "plan_csharp_regression_scope", ReadOnly = true, Idempotent = true)]
    [Description("Create a regression-scope plan by combining verification planning and semantic change review into smoke, focused, and broad command tiers.")]
    public Task<WorkspaceQueryResult<CSharpRegressionScopePlan>> PlanCSharpRegressionScope(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string[]? areaPaths = null,
        string? symbolQuery = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxRelatedTests = 20,
        int maxRelatedItems = 20,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.PlanCSharpRegressionScope(problemText, buildOutput, buildLogFilePath, changedFiles, areaPaths, symbolQuery, filePath, includePathPatterns, excludePathPatterns, projectName, minimumSeverity, noiseProfile, includeVisualStudioBuildOutput, maxVisualStudioBuildOutputCharacters, maxBuildIssues, maxDiagnostics, maxRelatedTests, maxRelatedItems, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "prepare_debug_session", ReadOnly = true, Idempotent = true)]
    [Description("Prepare a read-only debug-session package with health, current debugger status, breakpoints, call stack, source snippets, and explicit next actions.")]
    public Task<WorkspaceQueryResult<DebugSessionPreparationPlan>> PrepareDebugSession(
        int maxBreakpoints = 100,
        int maxFrames = 50,
        int maxSourceSnippets = 6,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.PrepareDebugSession(maxBreakpoints, maxFrames, maxSourceSnippets, contextLines, maxCharsPerSnippet, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "plan_csharp_debug_scenario", ReadOnly = true, Idempotent = true)]
    [Description("Plan a debug scenario as explicit configure/start/wait/collect/cleanup steps without mutating Visual Studio state.")]
    public Task<WorkspaceQueryResult<CSharpDebugScenarioPlan>> PlanCSharpDebugScenario(
        string? problemText = null,
        string? scenarioName = null,
        string? breakpointFilePath = null,
        int? breakpointLine = null,
        string[]? artifactPaths = null,
        bool stopDebuggingAtEnd = false,
        int maxBreakpoints = 100,
        int maxFrames = 50,
        int maxSourceSnippets = 6,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.PlanCSharpDebugScenario(problemText, scenarioName, breakpointFilePath, breakpointLine, artifactPaths, stopDebuggingAtEnd, maxBreakpoints, maxFrames, maxSourceSnippets, contextLines, maxCharsPerSnippet, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "analyze_csharp_repo_workflow", ReadOnly = true, Idempotent = true)]
    [Description("Analyze a C# repository workflow for solution selection, build/test commands, noisy paths, artifacts, and tool-routing rules.")]
    public Task<WorkspaceQueryResult<CSharpRepoWorkflowAnalysis>> AnalyzeCSharpRepoWorkflow(
        string? rootDirectory = null,
        string? preferredName = null,
        int maxDepth = 4,
        int maxSolutions = 8,
        int maxProjects = 80,
        CancellationToken cancellationToken = default)
    {
        return _inner.AnalyzeCSharpRepoWorkflow(rootDirectory, preferredName, maxDepth, maxSolutions, maxProjects, cancellationToken);
    }

    [McpServerTool(Name = "generate_csharp_agent_instructions", ReadOnly = true, Idempotent = true)]
    [Description("Generate a draft AGENTS/Copilot-style instruction document for a C# repository without writing files.")]
    public Task<WorkspaceQueryResult<CSharpAgentInstructionsDraft>> GenerateCSharpAgentInstructions(
        string? rootDirectory = null,
        string? preferredName = null,
        string? suggestedFileName = null,
        int maxDepth = 4,
        int maxSolutions = 8,
        int maxProjects = 80,
        CancellationToken cancellationToken = default)
    {
        return _inner.GenerateCSharpAgentInstructions(rootDirectory, preferredName, suggestedFileName, maxDepth, maxSolutions, maxProjects, cancellationToken);
    }

    [McpServerTool(Name = "split_csharp_agent_work", ReadOnly = true, Idempotent = true)]
    [Description("Split a C# objective into bounded child-agent work packets with file/symbol scope, allowed tools, budgets, and output schema.")]
    public Task<WorkspaceQueryResult<CSharpAgentWorkSplitPlan>> SplitCSharpAgentWork(
        string objective,
        string[]? changedFiles = null,
        string[]? scopeSymbols = null,
        string[]? evidenceResources = null,
        string[]? areaPaths = null,
        int maxPackets = 4,
        int maxFilesPerPacket = 8,
        CancellationToken cancellationToken = default)
    {
        return _inner.SplitCSharpAgentWork(objective, changedFiles, scopeSymbols, evidenceResources, areaPaths, maxPackets, maxFilesPerPacket, cancellationToken);
    }

    [McpServerTool(Name = "merge_csharp_agent_findings", ReadOnly = true, Idempotent = true)]
    [Description("Merge child-agent C# findings, detect missing packets, and produce a compact follow-up summary.")]
    public Task<WorkspaceQueryResult<CSharpAgentFindingMergeResult>> MergeCSharpAgentFindings(
        string? objective = null,
        string[]? expectedPacketIds = null,
        CSharpAgentFinding[]? findings = null,
        string[]? findingSummaries = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.MergeCSharpAgentFindings(objective, expectedPacketIds, findings, findingSummaries, cancellationToken);
    }

    [McpServerTool(Name = "get_csharp_workflow_performance_snapshot", ReadOnly = true, Idempotent = true)]
    [Description("Return a compact performance and workflow harness snapshot with active/stale bridge counts, tool count expectations, and suggested benchmark commands.")]
    public Task<WorkspaceQueryResult<CSharpWorkflowPerformanceSnapshot>> GetCSharpWorkflowPerformanceSnapshot(
        string? solutionPath = null,
        string? benchmarkProfile = null,
        int expectedToolCount = 83,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetCSharpWorkflowPerformanceSnapshot(solutionPath, benchmarkProfile, expectedToolCount, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }
}
