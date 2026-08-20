using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed partial class VisualStudioWorkspaceQueryService
{
    private static WorkspaceQueryResult<T> Success<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<string>? diagnostics = null,
        bool isPartial = false)
    {
        var effectiveDiagnostics = diagnostics ?? Array.Empty<string>();
        return new WorkspaceQueryResult<T>
        {
            Items = items,
            Diagnostics = effectiveDiagnostics,
            IsPartial = isPartial,
        };
    }

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = Array.Empty<T>(),
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }

    private static bool MatchesPathPattern(string filePath, string pattern)
    {
        var normalizedPath = NormalizePathForMatching(filePath);
        var normalizedPattern = NormalizePathForMatching(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
        {
            var escapedPattern = Regex.Escape(normalizedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            var regex = IsRootedPathPattern(normalizedPattern)
                ? "^" + escapedPattern + "$"
                : "(^|.*/)" + escapedPattern + "$";
            return Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase);
        }

        if (normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var directoryPattern = normalizedPattern.EndsWith("/", StringComparison.Ordinal)
            ? normalizedPattern
            : normalizedPattern + "/";

        return normalizedPath.StartsWith(directoryPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/" + directoryPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForMatching(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static bool IsRootedPathPattern(string pattern)
    {
        return pattern.StartsWith("/", StringComparison.Ordinal)
            || (pattern.Length >= 2 && pattern[1] == ':');
    }
}
