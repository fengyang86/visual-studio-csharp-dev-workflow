using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum WorkflowEvidenceLevel
{
    Unknown = 0,
    Fact = 1,
    Inference = 2,
    Heuristic = 3,
    Background = 4,
}

public enum WorkflowActionKind
{
    Unknown = 0,
    InspectFile = 1,
    InspectSymbol = 2,
    InspectDiagnostic = 3,
    RunBuild = 4,
    RunTests = 5,
    RequestBuildOutput = 6,
    NarrowScope = 7,
    BroadenScope = 8,
    IgnoreBackgroundNoise = 9,
    OpenVisualStudio = 10,
    SelectTarget = 11,
    InspectArtifact = 12,
    StartDebugging = 13,
    CollectDebugContext = 14,
    CleanDebugSession = 15,
}

public sealed class SymbolKey
{
    public SymbolKey(string value)
    {
        // Tolerate null/empty during deserialization so a single malformed field
        // cannot kill the whole envelope; producers guard upstream and WhenWritingNull
        // omits the member, so this only fires on genuinely malformed peers.
        Value = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value;
}

public sealed class SourceSpan
{
    public string FilePath { get; set; } = string.Empty;

    public int StartLine { get; set; }

    public int StartColumn { get; set; }

    public int EndLine { get; set; }

    public int EndColumn { get; set; }
}

public sealed class RecommendedNextAction
{
    public WorkflowActionKind Kind { get; set; }

    public WorkflowEvidenceLevel EvidenceLevel { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Confidence { get; set; } = string.Empty;

    public string TargetFilePath { get; set; } = string.Empty;

    public SourceSpan? TargetSpan { get; set; }

    public SymbolDescriptor? TargetSymbol { get; set; }

    public string TargetProjectName { get; set; } = string.Empty;

    public string SuggestedTool { get; set; } = string.Empty;

    public string SuggestedCommand { get; set; } = string.Empty;
}

public sealed class VerificationCommand
{
    public string Command { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Confidence { get; set; } = string.Empty;

    public string[] Reasons { get; set; } = Array.Empty<string>();
}

public sealed class VisualStudioBridgeTarget
{
    public string PipeName { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public string SolutionPath { get; set; } = string.Empty;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(PipeName)
        && string.IsNullOrWhiteSpace(InstanceId)
        && string.IsNullOrWhiteSpace(SolutionPath);
}

public interface IVisualStudioBridgeTargetedRequest
{
    VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class WorkspaceQueryResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    public IReadOnlyList<string> Diagnostics { get; set; } = Array.Empty<string>();

    public bool IsPartial { get; set; }

    // False only when the request itself failed (validation, transport, target
    // resolution, rejected mutation). Truncation and empty-but-valid results keep
    // Succeeded=true and signal through IsPartial/diagnostics instead, so clients
    // can machine-distinguish "retry with a narrower scope" from "rejected".
    public bool Succeeded { get; set; } = true;

    // Machine-readable failure code derived from the first diagnostic when it
    // follows the "Code: message" convention (e.g. "WorkspaceVersionChanged: ...").
    public string? ErrorCode => Succeeded || Diagnostics.Count == 0
        ? null
        : WorkspaceQueryDiagnostics.TryExtractErrorCode(Diagnostics[0]);
}

public static class WorkspaceQueryDiagnostics
{
    public static string? TryExtractErrorCode(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return null;
        }

        var separatorIndex = diagnostic.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex > 64)
        {
            return null;
        }

        var candidate = diagnostic.Substring(0, separatorIndex).Trim();
        if (candidate.Length == 0)
        {
            return null;
        }

        var hasLetter = false;
        foreach (var ch in candidate)
        {
            if (char.IsLetterOrDigit(ch))
            {
                hasLetter |= char.IsLetter(ch);
                continue;
            }

            if (ch == '-')
            {
                continue;
            }

            return null;
        }

        return hasLetter ? candidate : null;
    }
}
