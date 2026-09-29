using System;

namespace VisualStudio.CSharpNavigator.Protocol;

public static class WorkspaceTargetIdentity
{
    public static bool Conflicts(VisualStudioBridgeTarget requested, VisualStudioBridgeTarget known)
    {
        return Different(requested.PipeName, known.PipeName)
            || Different(requested.InstanceId, known.InstanceId)
            || Different(NormalizePath(requested.SolutionPath), NormalizePath(known.SolutionPath));
    }

    public static bool Matches(VisualStudioBridgeTarget requested, VisualStudioBridgeTarget known)
    {
        return MatchesValue(requested.PipeName, known.PipeName)
            && MatchesValue(requested.InstanceId, known.InstanceId)
            && MatchesValue(NormalizePath(requested.SolutionPath), NormalizePath(known.SolutionPath));
    }

    public static VisualStudioBridgeTarget Copy(VisualStudioBridgeTarget target)
    {
        return new VisualStudioBridgeTarget
        {
            PipeName = target.PipeName,
            InstanceId = target.InstanceId,
            SolutionPath = target.SolutionPath,
        };
    }

    private static bool Different(string requested, string known) =>
        !string.IsNullOrWhiteSpace(requested) && !string.IsNullOrWhiteSpace(known)
        && !string.Equals(requested, known, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesValue(string requested, string known) =>
        string.IsNullOrWhiteSpace(requested)
        || string.Equals(requested, known, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) => (path ?? string.Empty).Trim().Replace('/', '\\');
}
