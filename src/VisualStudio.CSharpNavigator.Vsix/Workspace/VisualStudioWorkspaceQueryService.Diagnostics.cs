using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    public async Task<WorkspaceQueryResult<CodeDiagnostic>> GetDiagnosticsAsync(
        DiagnosticsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 5000)
        {
            return Failure<CodeDiagnostic>("MaxResults must be between 1 and 5000.");
        }

        if (request.MaxProjects is < 0 or > 1000)
        {
            return Failure<CodeDiagnostic>("MaxProjects must be between 0 and 1000.");
        }

        if (request.MaxElapsedMilliseconds is < 1000 or > 55000)
        {
            return Failure<CodeDiagnostic>("MaxElapsedMilliseconds must be between 1000 and 55000.");
        }

        var solutionResult = await GetRequiredSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solutionResult.Failure is not null)
        {
            return solutionResult.Failure.As<CodeDiagnostic>();
        }

        var solution = solutionResult.Solution!;
        Document? requestedDocument = null;
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            requestedDocument = await FindDocumentByPathAsync(
                    solution,
                    request.FilePath!,
                    request.IncludeGeneratedCode,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.FilePath) && requestedDocument is null)
        {
            return Failure<CodeDiagnostic>("DocumentNotFound: the requested file is not in the active solution.");
        }

        var projects = solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .Where(project => string.IsNullOrWhiteSpace(request.ProjectName)
                || string.Equals(project.Name, request.ProjectName, StringComparison.OrdinalIgnoreCase))
            .Where(project => requestedDocument is null || project.Id == requestedDocument.Project.Id)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(request.ProjectName) && projects.Length == 0)
        {
            return Failure<CodeDiagnostic>("ProjectNotFound: no C# project matched the requested project name.");
        }

        var diagnostics = new List<string>();
        var items = new List<CodeDiagnostic>();
        var isPartial = false;
        var excludedByPathCount = 0;
        var filteredByNoiseProfileCount = 0;
        var processedProjectCount = 0;
        var projectsToProcess = request.MaxProjects == 0
            ? projects
            : projects.Take(request.MaxProjects).ToArray();
        if (projectsToProcess.Length < projects.Length)
        {
            diagnostics.Add($"DiagnosticsProjectLimit: processed {projectsToProcess.Length} of {projects.Length} matching C# project(s) because MaxProjects={request.MaxProjects}.");
            isPartial = true;
        }

        var elapsed = Stopwatch.StartNew();
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(TimeSpan.FromMilliseconds(request.MaxElapsedMilliseconds));
        foreach (var project in projectsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (elapsed.ElapsedMilliseconds >= request.MaxElapsedMilliseconds)
            {
                diagnostics.Add($"DiagnosticsTimeBudgetExceeded: processed {processedProjectCount} of {projects.Length} matching C# project(s) before MaxElapsedMilliseconds={request.MaxElapsedMilliseconds}.");
                isPartial = true;
                break;
            }

            Compilation? compilation;
            try
            {
                compilation = await project.GetCompilationAsync(budgetCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budgetCts.IsCancellationRequested)
            {
                diagnostics.Add($"DiagnosticsTimeBudgetExceeded: processed {processedProjectCount} of {projects.Length} matching C# project(s) before MaxElapsedMilliseconds={request.MaxElapsedMilliseconds}.");
                isPartial = true;
                break;
            }

            if (compilation is null)
            {
                diagnostics.Add($"Project '{project.Name}' has no compilation.");
                isPartial = true;
                processedProjectCount++;
                continue;
            }

            ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> projectDiagnostics;
            try
            {
                projectDiagnostics = await GetProjectDiagnosticsAsync(
                        project,
                        compilation,
                        diagnostics,
                        budgetCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budgetCts.IsCancellationRequested)
            {
                diagnostics.Add($"DiagnosticsTimeBudgetExceeded: processed {processedProjectCount} of {projects.Length} matching C# project(s) before MaxElapsedMilliseconds={request.MaxElapsedMilliseconds}.");
                isPartial = true;
                break;
            }

            var timeBudgetExceeded = false;
            foreach (var diagnostic in projectDiagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (budgetCts.IsCancellationRequested)
                {
                    diagnostics.Add($"DiagnosticsTimeBudgetExceeded: processed {processedProjectCount} of {projects.Length} matching C# project(s) before MaxElapsedMilliseconds={request.MaxElapsedMilliseconds}.");
                    isPartial = true;
                    timeBudgetExceeded = true;
                    break;
                }

                if (!MatchesDiagnosticScope(diagnostic, requestedDocument, request.IncludeGeneratedCode))
                {
                    continue;
                }

                if (IsDiagnosticPathExcluded(diagnostic, request.ExcludePathPatterns ?? Array.Empty<string>()))
                {
                    excludedByPathCount++;
                    continue;
                }

                if (ShouldFilterKnownNoiseDiagnostic(diagnostic, request))
                {
                    filteredByNoiseProfileCount++;
                    continue;
                }

                if (!IsDiagnosticPathIncluded(diagnostic, request.IncludePathPatterns ?? Array.Empty<string>()))
                {
                    continue;
                }

                if (!MatchesMinimumSeverity(diagnostic.Severity, request.MinimumSeverity))
                {
                    continue;
                }

                items.Add(CreateDiagnostic(project.Name, diagnostic, request));
            }

            if (timeBudgetExceeded)
            {
                break;
            }

            processedProjectCount++;
        }

        if (excludedByPathCount > 0)
        {
            diagnostics.Add($"DiagnosticsExcludedByPathFilters: {excludedByPathCount} diagnostic(s) were suppressed by excludePathPatterns.");
        }

        if (filteredByNoiseProfileCount > 0)
        {
            diagnostics.Add($"DiagnosticsFilteredByNoiseProfile: {filteredByNoiseProfileCount} known-noise diagnostic(s) were suppressed by noiseProfile={request.NoiseProfile}.");
        }

        if (DiagnosticsNoisePolicy.ShouldAutoFilterKnownNoise(request))
        {
            diagnostics.Add(DiagnosticsNoisePolicy.AutoNoiseFilterDiagnostic);
        }

        var rankedItems = items
            .OrderByDescending(item => item.RelevanceScore)
            .ThenByDescending(item => item.Severity)
            .ThenBy(item => item.Span?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Span?.StartLine ?? int.MaxValue)
            .ToArray();

        if (rankedItems.Length > request.MaxResults)
        {
            diagnostics.Add($"Diagnostics were ranked by relevance and truncated at MaxResults={request.MaxResults}.");
            isPartial = true;
            rankedItems = rankedItems.Take(request.MaxResults).ToArray();
        }

        if (rankedItems.Length > 0)
        {
            diagnostics.Add($"Diagnostics are ranked by relevance using changedFiles, filePath, includePathPatterns, projectName, severity, source location, and noiseProfile={request.NoiseProfile}.");
        }

        return Success(rankedItems, diagnostics, isPartial);
    }

    private static async Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> GetProjectDiagnosticsAsync(
        Project project,
        Compilation compilation,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var analyzers = ImmutableArray.CreateRange(
            project.AnalyzerReferences.SelectMany(reference => reference.GetAnalyzers(project.Language)));

        if (analyzers.Length == 0)
        {
            return compilation.GetDiagnostics(cancellationToken);
        }

        try
        {
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            return await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Add($"Analyzer diagnostics failed for project '{project.Name}': {ex.Message}. Compiler diagnostics were returned.");
            return compilation.GetDiagnostics(cancellationToken);
        }
    }

    private static bool MatchesDiagnosticScope(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        Document? requestedDocument,
        bool includeGeneratedCode)
    {
        if (diagnostic.Location.SourceTree is null)
        {
            return requestedDocument is null;
        }

        if (requestedDocument is not null)
        {
            return requestedDocument.TryGetSyntaxTree(out var requestedTree)
                && ReferenceEquals(diagnostic.Location.SourceTree, requestedTree);
        }

        return includeGeneratedCode || !IsGeneratedPath(diagnostic.Location.SourceTree.FilePath);
    }

    private static bool MatchesMinimumSeverity(
        Microsoft.CodeAnalysis.DiagnosticSeverity severity,
        CodeDiagnosticSeverity? minimumSeverity)
    {
        return minimumSeverity is null || MapDiagnosticSeverity(severity) >= minimumSeverity.Value;
    }

    private static bool MatchesDiagnosticPathFilters(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        IReadOnlyCollection<string> includePathPatterns,
        IReadOnlyCollection<string> excludePathPatterns)
    {
        if (includePathPatterns.Count == 0 && excludePathPatterns.Count == 0)
        {
            return true;
        }

        var filePath = diagnostic.Location.GetLineSpan().Path;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return includePathPatterns.Count == 0;
        }

        if (excludePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern)))
        {
            return false;
        }

        return includePathPatterns.Count == 0
            || includePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static bool IsDiagnosticPathExcluded(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        IReadOnlyCollection<string> excludePathPatterns)
    {
        return TryGetDiagnosticPath(diagnostic, out var filePath)
            && excludePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static bool IsDiagnosticPathIncluded(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        IReadOnlyCollection<string> includePathPatterns)
    {
        return includePathPatterns.Count == 0
            || (TryGetDiagnosticPath(diagnostic, out var filePath)
                && includePathPatterns.Any(pattern => MatchesPathPattern(filePath, pattern)));
    }

    private static bool ShouldFilterKnownNoiseDiagnostic(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        DiagnosticsRequest request)
    {
        return TryGetDiagnosticPath(diagnostic, out var filePath)
            && DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(request, filePath);
    }

    private static bool TryGetDiagnosticPath(
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        out string filePath)
    {
        filePath = diagnostic.Location.GetLineSpan().Path;
        return !string.IsNullOrWhiteSpace(filePath);
    }

    private static CodeDiagnostic CreateDiagnostic(
        string projectName,
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        DiagnosticsRequest request)
    {
        var severity = MapDiagnosticSeverity(diagnostic.Severity);
        var span = CreateSpan(diagnostic.Location);
        return new CodeDiagnostic
        {
            Id = diagnostic.Id,
            Title = diagnostic.Descriptor.Title.ToString(),
            Message = diagnostic.GetMessage(),
            Severity = severity,
            WarningLevel = diagnostic.WarningLevel,
            ProjectName = projectName,
            Span = span,
            RelevanceScore = ComputeDiagnosticRelevanceScore(projectName, diagnostic, severity, span, request),
            ScopeReasons = CreateDiagnosticScopeReasons(projectName, diagnostic, severity, span, request),
        };
    }

    private static int ComputeDiagnosticRelevanceScore(
        string projectName,
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        CodeDiagnosticSeverity severity,
        SourceSpan? span,
        DiagnosticsRequest request)
    {
        var score = severity switch
        {
            CodeDiagnosticSeverity.Error => 100,
            CodeDiagnosticSeverity.Warning => 40,
            CodeDiagnosticSeverity.Info => 10,
            CodeDiagnosticSeverity.Hidden => 0,
            _ => 0,
        };

        if (span is null)
        {
            score -= 20;
            return score;
        }

        score += 10;
        if (MatchesAnyPathPattern(span.FilePath, request.ChangedFiles))
        {
            score += 80;
        }

        if (!string.IsNullOrWhiteSpace(request.FilePath)
            && MatchesPathPattern(span.FilePath, request.FilePath!))
        {
            score += 70;
        }

        if (TryGetFirstMatchingPattern(span.FilePath, request.IncludePathPatterns, out _))
        {
            score += 50;
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectName)
            && string.Equals(projectName, request.ProjectName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (DiagnosticsNoisePolicy.ShouldMarkKnownNoisePath(request, span.FilePath))
        {
            score -= 40;
        }

        return score;
    }

    private static string[] CreateDiagnosticScopeReasons(
        string projectName,
        Microsoft.CodeAnalysis.Diagnostic diagnostic,
        CodeDiagnosticSeverity severity,
        SourceSpan? span,
        DiagnosticsRequest request)
    {
        var reasons = new List<string>
        {
            severity switch
            {
                CodeDiagnosticSeverity.Error => "severity: error",
                CodeDiagnosticSeverity.Warning => "severity: warning",
                CodeDiagnosticSeverity.Info => "severity: info",
                CodeDiagnosticSeverity.Hidden => "severity: hidden",
                _ => "severity: unknown",
            },
        };

        if (!string.IsNullOrWhiteSpace(request.ProjectName)
            && string.Equals(projectName, request.ProjectName, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("project scope");
        }

        if (span is null)
        {
            reasons.Add("project/global diagnostic");
            return reasons.ToArray();
        }

        reasons.Add("source location");
        if (MatchesAnyPathPattern(span.FilePath, request.ChangedFiles))
        {
            reasons.Add("changed file");
        }

        if (!string.IsNullOrWhiteSpace(request.FilePath)
            && MatchesPathPattern(span.FilePath, request.FilePath!))
        {
            reasons.Add("requested file");
        }

        if (TryGetFirstMatchingPattern(span.FilePath, request.IncludePathPatterns, out var includePattern))
        {
            reasons.Add($"include pattern: {includePattern}");
        }

        if (DiagnosticsNoisePolicy.ShouldMarkKnownNoisePath(request, span.FilePath))
        {
            reasons.Add("known noise path");
        }

        if (diagnostic.Location.SourceTree is null)
        {
            reasons.Add("no source tree");
        }

        return reasons.ToArray();
    }

    private static bool MatchesAnyPathPattern(string filePath, IReadOnlyCollection<string>? patterns)
    {
        return patterns is not null
            && patterns.Any(pattern => MatchesPathPattern(filePath, pattern));
    }

    private static bool TryGetFirstMatchingPattern(
        string filePath,
        IReadOnlyCollection<string>? patterns,
        out string matchingPattern)
    {
        matchingPattern = string.Empty;
        if (patterns is null)
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (MatchesPathPattern(filePath, pattern))
            {
                matchingPattern = pattern;
                return true;
            }
        }

        return false;
    }

    private static CodeDiagnosticSeverity MapDiagnosticSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity)
    {
        return severity switch
        {
            Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden => CodeDiagnosticSeverity.Hidden,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Info => CodeDiagnosticSeverity.Info,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => CodeDiagnosticSeverity.Warning,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error => CodeDiagnosticSeverity.Error,
            _ => CodeDiagnosticSeverity.Hidden,
        };
    }
}
