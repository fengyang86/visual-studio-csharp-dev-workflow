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
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Symbol key cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

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
}
