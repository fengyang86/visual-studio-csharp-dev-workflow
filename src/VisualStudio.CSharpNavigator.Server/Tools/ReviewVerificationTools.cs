using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class ReviewVerificationTools
{
    private readonly CodeNavigationTools _inner;

    public ReviewVerificationTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "start_csharp_investigation", ReadOnly = true, Idempotent = true)]
    [Description("Start a read-only C# investigation by combining bridge health, optional build-log triage, scoped diagnostics, symbol lookup, and related context.")]
    public Task<WorkspaceQueryResult<CSharpInvestigationReport>> StartCSharpInvestigation(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string? symbolQuery = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string[]? changedFiles = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeWholeSolutionDiagnostics = false,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxSymbols = 20,
        int maxRelatedItems = 20,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.StartCSharpInvestigation(problemText, buildOutput, buildLogFilePath, symbolQuery, filePath, includePathPatterns, excludePathPatterns, changedFiles, projectName, minimumSeverity, noiseProfile, includeWholeSolutionDiagnostics, includeVisualStudioBuildOutput, maxVisualStudioBuildOutputCharacters, maxBuildIssues, maxDiagnostics, maxSymbols, maxRelatedItems, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "get_csharp_task_context", ReadOnly = true, Idempotent = true)]
    [Description("Create a compact task context package for C# work by combining investigation evidence with bounded source snippets for likely edit locations.")]
    public Task<WorkspaceQueryResult<CSharpTaskContextPackage>> GetCSharpTaskContext(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string? symbolQuery = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string[]? changedFiles = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeWholeSolutionDiagnostics = false,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxSymbols = 20,
        int maxRelatedItems = 20,
        int maxSourceSnippets = 8,
        int contextLines = 3,
        int maxCharsPerSnippet = 8000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetCSharpTaskContext(problemText, buildOutput, buildLogFilePath, symbolQuery, filePath, includePathPatterns, excludePathPatterns, changedFiles, projectName, minimumSeverity, noiseProfile, includeWholeSolutionDiagnostics, includeVisualStudioBuildOutput, maxVisualStudioBuildOutputCharacters, maxBuildIssues, maxDiagnostics, maxSymbols, maxRelatedItems, maxSourceSnippets, contextLines, maxCharsPerSnippet, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "plan_csharp_verification", ReadOnly = true, Idempotent = true)]
    [Description("Create a read-only verification plan for changed C# files by combining build triage, scoped diagnostics, affected projects, related tests, and dotnet command suggestions.")]
    public Task<WorkspaceQueryResult<CSharpVerificationPlan>> PlanCSharpVerification(
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string? symbolQuery = null,
        string? filePath = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        bool includeWholeSolutionDiagnostics = false,
        bool includeVisualStudioBuildOutput = true,
        int maxVisualStudioBuildOutputCharacters = 20000,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxRelatedTests = 20,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.PlanCSharpVerification(buildOutput, buildLogFilePath, changedFiles, symbolQuery, filePath, includePathPatterns, excludePathPatterns, projectName, minimumSeverity, noiseProfile, includeWholeSolutionDiagnostics, includeVisualStudioBuildOutput, maxVisualStudioBuildOutputCharacters, maxBuildIssues, maxDiagnostics, maxRelatedTests, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "audit_csharp_area", ReadOnly = true, Idempotent = true)]
    [Description("Create a compact read-only audit package for a C# area by combining area paths, query terms, scoped diagnostics, symbol candidates, related tests, temporary markers, and next actions.")]
    public Task<WorkspaceQueryResult<CSharpAreaAuditReport>> AuditCSharpArea(
        string? problemText = null,
        string[]? areaPaths = null,
        string[]? queryTerms = null,
        string[]? testPatterns = null,
        string[]? changedFiles = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        string? projectName = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        int maxDiagnostics = 30,
        int maxSymbols = 30,
        int maxRelatedTests = 20,
        int maxMarkers = 20,
        int maxFiles = 20,
        int maxDiagnosticProjects = 0,
        int maxDiagnosticElapsedMilliseconds = 30000,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.AuditCSharpArea(problemText, areaPaths, queryTerms, testPatterns, changedFiles, includePathPatterns, excludePathPatterns, projectName, minimumSeverity, noiseProfile, maxDiagnostics, maxSymbols, maxRelatedTests, maxMarkers, maxFiles, maxDiagnosticProjects, maxDiagnosticElapsedMilliseconds, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "review_csharp_change", ReadOnly = true, Idempotent = true)]
    [Description("Create a read-only semantic review package for changed C# files or a focused symbol, including impact, risks, test gaps, temporary markers, and verification next actions.")]
    public Task<WorkspaceQueryResult<CSharpChangeReviewReport>> ReviewCSharpChange(
        string? problemText = null,
        string? buildOutput = null,
        string? buildLogFilePath = null,
        string[]? changedFiles = null,
        string[]? areaPaths = null,
        string? symbolQuery = null,
        string? projectName = null,
        string[]? includePathPatterns = null,
        string[]? excludePathPatterns = null,
        CodeDiagnosticSeverity? minimumSeverity = CodeDiagnosticSeverity.Warning,
        CodeDiagnosticNoiseProfile noiseProfile = CodeDiagnosticNoiseProfile.Auto,
        int maxBuildIssues = 20,
        int maxDiagnostics = 30,
        int maxSymbols = 20,
        int maxRelatedItems = 20,
        int maxFiles = 20,
        bool includeGeneratedCode = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.ReviewCSharpChange(problemText, buildOutput, buildLogFilePath, changedFiles, areaPaths, symbolQuery, projectName, includePathPatterns, excludePathPatterns, minimumSeverity, noiseProfile, maxBuildIssues, maxDiagnostics, maxSymbols, maxRelatedItems, maxFiles, includeGeneratedCode, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }
}
