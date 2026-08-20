using System.Text.RegularExpressions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

internal static partial class BuildLogTriage
{
    private const int MaxInputLength = 1_000_000;
    private static readonly Regex VisualStudioProjectPrefixRegex = new(@"^\d+>", RegexOptions.CultureInvariant);

    public static WorkspaceQueryResult<BuildTriageReport> Analyze(
        string buildOutput,
        string[] includePathPatterns,
        string[] excludePathPatterns,
        string[] changedFiles,
        int maxResults)
    {
        if (string.IsNullOrWhiteSpace(buildOutput))
        {
            return Failure("Build output is required.");
        }

        if (maxResults is < 1 or > 500)
        {
            return Failure("MaxResults must be between 1 and 500.");
        }

        var diagnostics = new List<string>();
        if (buildOutput.Length > MaxInputLength)
        {
            buildOutput = buildOutput.Substring(0, MaxInputLength);
            diagnostics.Add($"Build output was truncated to {MaxInputLength} characters before triage.");
        }

        var normalizedChangedFiles = changedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parsedIssues = ParseIssues(buildOutput)
            .Select(issue => MarkChangedFile(issue, normalizedChangedFiles))
            .ToArray();
        var filteredIssues = new List<ScoredBuildIssue>();
        var suppressedIssueCount = 0;

        foreach (var issue in parsedIssues)
        {
            var filePath = issue.Issue.Span?.FilePath ?? string.Empty;
            if (!IsIncluded(filePath, includePathPatterns) || IsExcluded(filePath, excludePathPatterns))
            {
                suppressedIssueCount += issue.Issue.OccurrenceCount;
                continue;
            }

            filteredIssues.Add(issue);
        }

        var scoredVisibleIssues = filteredIssues
            .Select(Score)
            .ToArray();
        var hasPrimaryCandidate = scoredVisibleIssues.Any(issue => IsPrimaryRootCauseCandidate(issue.Issue));
        foreach (var scoredIssue in scoredVisibleIssues)
        {
            AnnotateRootCause(scoredIssue, hasPrimaryCandidate);
        }

        var orderedIssues = scoredVisibleIssues
            .OrderByDescending(issue => issue.Score)
            .ThenBy(issue => issue.Issue.Span?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Issue.Span?.StartLine ?? int.MaxValue)
            .Take(maxResults)
            .Select(issue => issue.Issue)
            .ToArray();

        var allVisibleIssues = filteredIssues.Select(issue => issue.Issue).ToArray();
        var report = new BuildTriageReport
        {
            Issues = orderedIssues,
            TotalIssueCount = parsedIssues.Sum(issue => issue.Issue.OccurrenceCount),
            ErrorCount = allVisibleIssues
                .Where(issue => issue.Severity == CodeDiagnosticSeverity.Error)
                .Sum(issue => issue.OccurrenceCount),
            WarningCount = allVisibleIssues
                .Where(issue => issue.Severity == CodeDiagnosticSeverity.Warning)
                .Sum(issue => issue.OccurrenceCount),
            SuppressedIssueCount = suppressedIssueCount,
            CascadeIssueCount = allVisibleIssues
                .Where(issue => issue.IsLikelyCascade)
                .Sum(issue => issue.OccurrenceCount),
            RecommendedNextActions = CreateReportRecommendedNextActions(orderedIssues),
            SuggestedNextSteps = CreateSuggestedNextSteps(orderedIssues, suppressedIssueCount),
        };

        return new WorkspaceQueryResult<BuildTriageReport>
        {
            Items = new[] { report },
            Diagnostics = diagnostics.ToArray(),
            IsPartial = parsedIssues.Length > maxResults || diagnostics.Count > 0,
        };
    }

    private static IEnumerable<ScoredBuildIssue> ParseIssues(string buildOutput)
    {
        var issuesByKey = new Dictionary<string, BuildIssue>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in SplitLines(buildOutput))
        {
            var line = NormalizeBuildOutputLine(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            var issue = TryParseIssue(line);
            if (issue is null)
            {
                continue;
            }

            var key = CreateIssueKey(issue);
            if (issuesByKey.TryGetValue(key, out var existing))
            {
                existing.OccurrenceCount++;
                continue;
            }

            issuesByKey.Add(key, issue);
        }

        foreach (var issue in issuesByKey.Values)
        {
            yield return new ScoredBuildIssue(issue, 0);
        }
    }

    private static string NormalizeBuildOutputLine(string rawLine)
    {
        var line = rawLine.Trim();
        while (true)
        {
            var match = VisualStudioProjectPrefixRegex.Match(line);
            if (!match.Success)
            {
                return line;
            }

            line = line.Substring(match.Length).TrimStart();
        }
    }

    private static BuildIssue? TryParseIssue(string line)
    {
        var match = FileIssueRegex().Match(line);
        if (!match.Success)
        {
            match = ProjectIssueRegex().Match(line);
        }

        if (!match.Success)
        {
            return null;
        }

        var severity = ParseSeverity(match.Groups["severity"].Value);
        var id = NormalizeIssueId(match.Groups["id"].Value, severity);
        var message = match.Groups["message"].Value.Trim();
        var filePath = match.Groups["path"].Value.Trim();
        var project = match.Groups["project"].Success
            ? match.Groups["project"].Value.Trim()
            : string.Empty;

        return new BuildIssue
        {
            Id = id,
            Kind = ClassifyKind(id, message),
            Severity = severity,
            Message = message,
            ProjectName = project,
            Span = CreateSpan(
                filePath,
                match.Groups["line"].Value,
                match.Groups["column"].Value,
                match.Groups["endLine"].Value,
                match.Groups["endColumn"].Value),
            RawText = line,
            IsLikelyCascade = IsLikelyCascade(id, message),
        };
    }

    private static SourceSpan? CreateSpan(
        string filePath,
        string lineValue,
        string columnValue,
        string endLineValue,
        string endColumnValue)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var line = int.TryParse(lineValue, out var parsedLine) && parsedLine > 0 ? parsedLine : 1;
        var column = int.TryParse(columnValue, out var parsedColumn) && parsedColumn > 0 ? parsedColumn : 1;
        var endLine = int.TryParse(endLineValue, out var parsedEndLine) && parsedEndLine > 0 ? parsedEndLine : line;
        var endColumn = int.TryParse(endColumnValue, out var parsedEndColumn) && parsedEndColumn > 0 ? parsedEndColumn : column;
        if (endLine < line)
        {
            endLine = line;
        }

        if (endLine == line && endColumn < column)
        {
            endColumn = column;
        }

        return new SourceSpan
        {
            FilePath = filePath,
            StartLine = line,
            StartColumn = column,
            EndLine = endLine,
            EndColumn = endColumn,
        };
    }

    private static CodeDiagnosticSeverity ParseSeverity(string value)
    {
        return string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase)
            ? CodeDiagnosticSeverity.Warning
            : CodeDiagnosticSeverity.Error;
    }

    private static string NormalizeIssueId(string value, CodeDiagnosticSeverity severity)
    {
        var id = value.Trim();
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return severity == CodeDiagnosticSeverity.Warning
            ? "BUILDWARNING"
            : "BUILDERROR";
    }

    private static BuildIssueKind ClassifyKind(string id, string message)
    {
        if (string.Equals(id, "BUILDERROR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "BUILDWARNING", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.MsBuild;
        }

        if (IsTargetFrameworkIssue(id, message))
        {
            return BuildIssueKind.TargetFramework;
        }

        if (IsRestoreIssue(id, message))
        {
            return BuildIssueKind.Restore;
        }

        if (IsGeneratedOutputIssue(id, message))
        {
            return BuildIssueKind.GeneratedOutput;
        }

        if (id.StartsWith("CS", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.Compiler;
        }

        if (id.StartsWith("MSB", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.MsBuild;
        }

        if (id.StartsWith("NU", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.NuGet;
        }

        if (id.StartsWith("NETSDK", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.Sdk;
        }

        if (id.StartsWith("CA", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("IDE", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIssueKind.Analyzer;
        }

        return BuildIssueKind.Unknown;
    }

    private static bool IsRestoreIssue(string id, string message)
    {
        if (id.StartsWith("NU", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(id, "NU1101", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "NU1102", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "NU1107", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "NU1301", StringComparison.OrdinalIgnoreCase)
                || message.Contains("restore", StringComparison.OrdinalIgnoreCase)
                || message.Contains("package", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(id, "MSB4236", StringComparison.OrdinalIgnoreCase)
            || message.Contains("assets file", StringComparison.OrdinalIgnoreCase)
            || message.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Run a NuGet package restore", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetFrameworkIssue(string id, string message)
    {
        return string.Equals(id, "NU1202", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "NETSDK1005", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "NETSDK1045", StringComparison.OrdinalIgnoreCase)
            || message.Contains("target framework", StringComparison.OrdinalIgnoreCase)
            || message.Contains("TargetFramework", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not compatible with", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedOutputIssue(string id, string message)
    {
        if (string.Equals(id, "CS2001", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "MSB3030", StringComparison.OrdinalIgnoreCase))
        {
            return message.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
                || message.Contains("because it was not found", StringComparison.OrdinalIgnoreCase);
        }

        return message.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            && message.Contains("could not be found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyCascade(string id, string message)
    {
        if (string.Equals(id, "CS0006", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(id, "MSB3073", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return message.Contains("metadata file", StringComparison.OrdinalIgnoreCase)
            && message.Contains("could not be found", StringComparison.OrdinalIgnoreCase);
    }

    private static ScoredBuildIssue MarkChangedFile(
        ScoredBuildIssue scoredIssue,
        ISet<string> changedFiles)
    {
        var filePath = scoredIssue.Issue.Span?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || changedFiles.Count == 0)
        {
            return scoredIssue;
        }

        var normalizedPath = NormalizePath(filePath);
        scoredIssue.Issue.IsInChangedFile = changedFiles.Contains(normalizedPath)
            || changedFiles.Any(changedFile => normalizedPath.EndsWith(changedFile, StringComparison.OrdinalIgnoreCase));
        return scoredIssue;
    }

    private static ScoredBuildIssue Score(ScoredBuildIssue scoredIssue)
    {
        var issue = scoredIssue.Issue;
        var score = 0;
        var reasons = new List<string>();

        if (issue.Severity == CodeDiagnosticSeverity.Error)
        {
            score += 100;
            reasons.Add("error");
        }
        else
        {
            score += 20;
            reasons.Add("warning");
        }

        if (issue.IsInChangedFile)
        {
            score += 60;
            reasons.Add("changed file");
        }

        if (issue.IsLikelyCascade)
        {
            score -= 50;
            reasons.Add("likely cascade");
        }
        else
        {
            score += 25;
            reasons.Add("primary candidate");
        }

        if (issue.Span is not null)
        {
            score += 10;
            reasons.Add("source location");
        }

        switch (issue.Kind)
        {
            case BuildIssueKind.Restore:
                score += 35;
                reasons.Add("restore failure");
                break;
            case BuildIssueKind.TargetFramework:
                score += 35;
                reasons.Add("target framework");
                break;
            case BuildIssueKind.GeneratedOutput:
                score += 20;
                reasons.Add("generated output");
                break;
        }

        issue.RankReasons = reasons.ToArray();
        return scoredIssue with { Score = score };
    }

    private static bool IsPrimaryRootCauseCandidate(BuildIssue issue)
    {
        return !issue.IsLikelyCascade
            && issue.Severity == CodeDiagnosticSeverity.Error
            && issue.Kind != BuildIssueKind.Unknown;
    }

    private static void AnnotateRootCause(ScoredBuildIssue scoredIssue, bool hasPrimaryCandidate)
    {
        var issue = scoredIssue.Issue;
        issue.IsRootCauseCandidate = IsPrimaryRootCauseCandidate(issue)
            || (!hasPrimaryCandidate && issue.Severity == CodeDiagnosticSeverity.Error);
        issue.RootCauseScore = scoredIssue.Score;

        var reasons = issue.RankReasons.ToList();
        if (issue.IsRootCauseCandidate)
        {
            reasons.Add(hasPrimaryCandidate || !issue.IsLikelyCascade
                ? "root cause candidate"
                : "fallback root cause candidate");
        }
        else if (issue.IsLikelyCascade && hasPrimaryCandidate)
        {
            reasons.Add("cascade after primary candidate");
        }

        issue.RankReasons = reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        issue.RecommendedNextActions = CreateIssueRecommendedNextActions(issue, hasPrimaryCandidate);
    }

    private static RecommendedNextAction[] CreateIssueRecommendedNextActions(BuildIssue issue, bool hasPrimaryCandidate)
    {
        var actions = new List<RecommendedNextAction>();
        var targetFilePath = issue.Span?.FilePath ?? string.Empty;
        var confidence = issue.IsRootCauseCandidate ? "high" : "medium";

        if (issue.IsLikelyCascade && hasPrimaryCandidate)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.IgnoreBackgroundNoise,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Confidence = "high",
                Reason = $"{issue.Id} looks like a downstream cascade; inspect earlier root-cause candidates first.",
                TargetFilePath = targetFilePath,
                TargetSpan = issue.Span,
                TargetProjectName = issue.ProjectName,
            });
            return actions.ToArray();
        }

        switch (issue.Kind)
        {
            case BuildIssueKind.Restore:
                actions.Add(new RecommendedNextAction
                {
                    Kind = WorkflowActionKind.RunBuild,
                    EvidenceLevel = WorkflowEvidenceLevel.Fact,
                    Confidence = confidence,
                    Reason = $"Resolve restore issue {issue.Id} by checking package id/version, configured package sources, and project.assets.json freshness.",
                    TargetFilePath = targetFilePath,
                    TargetSpan = issue.Span,
                    TargetProjectName = issue.ProjectName,
                    SuggestedCommand = string.IsNullOrWhiteSpace(issue.ProjectName)
                        ? "dotnet restore"
                        : $"dotnet restore {QuoteArgument(issue.ProjectName)}",
                });
                break;
            case BuildIssueKind.TargetFramework:
                actions.Add(new RecommendedNextAction
                {
                    Kind = WorkflowActionKind.InspectFile,
                    EvidenceLevel = WorkflowEvidenceLevel.Fact,
                    Confidence = confidence,
                    Reason = $"Inspect SDK, TargetFramework/TargetFrameworks, and package compatibility for {issue.Id}.",
                    TargetFilePath = targetFilePath,
                    TargetSpan = issue.Span,
                    TargetProjectName = issue.ProjectName,
                    SuggestedTool = "get_csharp_workspace_status",
                });
                break;
            case BuildIssueKind.GeneratedOutput:
                actions.Add(new RecommendedNextAction
                {
                    Kind = WorkflowActionKind.InspectFile,
                    EvidenceLevel = WorkflowEvidenceLevel.Inference,
                    Confidence = confidence,
                    Reason = $"Inspect generator/build-target inputs before chasing generated output issue {issue.Id}.",
                    TargetFilePath = targetFilePath,
                    TargetSpan = issue.Span,
                    TargetProjectName = issue.ProjectName,
                    SuggestedTool = "list_csharp_generated_documents",
                });
                break;
            case BuildIssueKind.Compiler:
            case BuildIssueKind.Analyzer:
                actions.Add(new RecommendedNextAction
                {
                    Kind = WorkflowActionKind.InspectDiagnostic,
                    EvidenceLevel = WorkflowEvidenceLevel.Fact,
                    Confidence = confidence,
                    Reason = $"Inspect source context for {issue.Id} at the diagnostic span.",
                    TargetFilePath = targetFilePath,
                    TargetSpan = issue.Span,
                    TargetProjectName = issue.ProjectName,
                    SuggestedTool = issue.Span is null ? "search_csharp_symbols" : "get_csharp_enclosing_context",
                });
                break;
            default:
                actions.Add(new RecommendedNextAction
                {
                    Kind = WorkflowActionKind.InspectDiagnostic,
                    EvidenceLevel = WorkflowEvidenceLevel.Fact,
                    Confidence = issue.IsRootCauseCandidate ? "medium" : "low",
                    Reason = $"Inspect build diagnostic {issue.Id} before broadening diagnostics scope.",
                    TargetFilePath = targetFilePath,
                    TargetSpan = issue.Span,
                    TargetProjectName = issue.ProjectName,
                    SuggestedTool = issue.Span is null ? "analyze_csharp_build_errors" : "get_csharp_enclosing_context",
                });
                break;
        }

        return actions.ToArray();
    }

    private static RecommendedNextAction[] CreateReportRecommendedNextActions(BuildIssue[] issues)
    {
        return issues
            .SelectMany(issue => issue.RecommendedNextActions)
            .GroupBy(
                action => string.Join("|", action.Kind.ToString(), action.SuggestedTool, action.SuggestedCommand, action.TargetFilePath),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToArray();
    }

    private static string[] CreateSuggestedNextSteps(BuildIssue[] issues, int suppressedIssueCount)
    {
        var nextSteps = new List<string>();
        var firstPrimary = issues.FirstOrDefault(issue => !issue.IsLikelyCascade)
            ?? issues.FirstOrDefault();

        if (firstPrimary is null)
        {
            nextSteps.Add("No MSBuild or compiler diagnostics were recognized. Capture build output with normal or minimal verbosity and retry.");
        }
        else if (firstPrimary.Span is not null)
        {
            nextSteps.Add($"Start with {firstPrimary.Id} at {firstPrimary.Span.FilePath}:{firstPrimary.Span.StartLine}:{firstPrimary.Span.StartColumn}.");
        }
        else
        {
            nextSteps.Add($"Start with {firstPrimary.Id}: {firstPrimary.Message}");
        }

        if (issues.Any(issue => issue.IsLikelyCascade))
        {
            nextSteps.Add("Inspect primary candidates before cascade errors such as missing metadata outputs.");
        }

        if (issues.Any(issue => issue.Kind == BuildIssueKind.Restore))
        {
            nextSteps.Add("For restore failures, inspect package source connectivity, package IDs/versions, and project.assets.json freshness before editing C# code.");
        }

        if (issues.Any(issue => issue.Kind == BuildIssueKind.TargetFramework))
        {
            nextSteps.Add("For target-framework failures, inspect TargetFramework/TargetFrameworks, SDK version, and package compatibility before chasing downstream compiler errors.");
        }

        if (issues.Any(issue => issue.Kind == BuildIssueKind.GeneratedOutput))
        {
            nextSteps.Add("For missing generated outputs, inspect source generator/build target inputs and obj/bin cleanup before treating downstream missing metadata errors as primary.");
        }

        if (issues.Any(issue => issue.IsInChangedFile))
        {
            nextSteps.Add("Prioritize issues in changed files before unrelated solution-wide failures.");
        }

        if (suppressedIssueCount > 0)
        {
            nextSteps.Add($"{suppressedIssueCount} issue occurrence(s) were suppressed by path filters.");
        }

        return nextSteps.ToArray();
    }

    private static bool IsIncluded(string filePath, string[] includePathPatterns)
    {
        return includePathPatterns.Length == 0
            || MatchesAnyPattern(filePath, includePathPatterns);
    }

    private static bool IsExcluded(string filePath, string[] excludePathPatterns)
    {
        return excludePathPatterns.Length > 0
            && MatchesAnyPattern(filePath, excludePathPatterns);
    }

    private static bool MatchesAnyPattern(string filePath, IEnumerable<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = NormalizePath(filePath);
        return patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(NormalizePath)
            .Any(pattern => normalizedPath.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || GlobToRegex(pattern).IsMatch(normalizedPath));
    }

    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Trim('"').Replace('\\', '/');
    }

    private static string QuoteArgument(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string CreateIssueKey(BuildIssue issue)
    {
        var filePath = issue.Span?.FilePath ?? string.Empty;
        var line = issue.Span?.StartLine ?? 0;
        return $"{issue.Id}|{filePath}|{line}|{issue.Message}";
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static WorkspaceQueryResult<BuildTriageReport> Failure(string diagnostic)
    {
        return new WorkspaceQueryResult<BuildTriageReport>
        {
            Items = Array.Empty<BuildTriageReport>(),
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }

    [GeneratedRegex(@"^(?<path>.+?)\((?<line>\d+)(?:,(?<column>\d+)(?:,(?<endLine>\d+),(?<endColumn>\d+))?)?\):\s*(?<severity>error|warning)(?:\s+(?<id>[A-Z]+\d+))?\s*:\s*(?<message>.*?)(?:\s+\[(?<project>.+?)\])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileIssueRegex();

    [GeneratedRegex(@"^(?:(?<path>.*?):\s*)?(?<severity>error|warning)(?:\s+(?<id>[A-Z]+\d+))?\s*:\s*(?<message>.*?)(?:\s+\[(?<project>.+?)\])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIssueRegex();

    private sealed record ScoredBuildIssue(BuildIssue Issue, int Score);
}
