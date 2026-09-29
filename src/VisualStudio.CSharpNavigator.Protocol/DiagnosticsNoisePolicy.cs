using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisualStudio.CSharpNavigator.Protocol;

public static class DiagnosticsNoisePolicy
{
    public const string AutoNoiseFilterDiagnostic =
        "DiagnosticsAutoNoiseFilter: unscoped diagnostics used noiseProfile=Auto; known-noise paths are suppressed. Pass filePath, changedFiles, includePathPatterns, or projectName to focus diagnostics, or noiseProfile=Off for a full audit.";

    public static bool HasFocusedScope(DiagnosticsRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.FilePath)
            || !string.IsNullOrWhiteSpace(request.ProjectName)
            || HasAny(request.IncludePathPatterns)
            || HasAny(request.ChangedFiles);
    }

    public static bool ShouldAutoFilterKnownNoise(DiagnosticsRequest request)
    {
        return request.NoiseProfile == CodeDiagnosticNoiseProfile.Auto
            && !HasFocusedScope(request);
    }

    public static bool ShouldFilterKnownNoisePath(DiagnosticsRequest request, string filePath)
    {
        if (!IsKnownNoisePath(filePath, request.NoisePathPatterns))
        {
            return false;
        }

        return request.NoiseProfile == CodeDiagnosticNoiseProfile.Filter
            || ShouldAutoFilterKnownNoise(request);
    }

    public static bool ShouldMarkKnownNoisePath(DiagnosticsRequest request, string filePath)
    {
        return request.NoiseProfile != CodeDiagnosticNoiseProfile.Off
            && IsKnownNoisePath(filePath, request.NoisePathPatterns);
    }

    public static bool IsKnownNoisePath(string filePath, IEnumerable<string>? configuredPatterns = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalized = NormalizePathForMatching(filePath);
        var fileName = Path.GetFileName(filePath.Replace('/', '\\'));
        return ContainsPathSegment(normalized, "obj")
            || ContainsPathSegment(normalized, "bin")
            || ContainsPathSegment(normalized, "generated")
            || ContainsPathSegment(normalized, "vendor")
            || ContainsPathSegment(normalized, "packages")
            || (configuredPatterns is not null
                && configuredPatterns.Any(pattern => MatchesPathPattern(normalized, pattern)))
            || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAny(string[]? values)
    {
        return values is not null
            && values.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool ContainsPathSegment(string normalizedPath, string segment)
    {
        return string.Equals(normalizedPath, segment, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/" + segment, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.IndexOf("/" + segment + "/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizePathForMatching(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static bool MatchesPathPattern(string normalizedPath, string pattern)
    {
        var normalizedPattern = NormalizePathForMatching(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
        {
            var escaped = System.Text.RegularExpressions.Regex.Escape(normalizedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return System.Text.RegularExpressions.Regex.IsMatch(
                normalizedPath,
                "(^|.*/)" + escaped + "($|/.*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return normalizedPath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedPattern + "/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.IndexOf("/" + normalizedPattern + "/", StringComparison.OrdinalIgnoreCase) >= 0
            || normalizedPath.EndsWith("/" + normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }
}
