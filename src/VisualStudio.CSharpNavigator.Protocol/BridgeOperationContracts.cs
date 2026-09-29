using System;

namespace VisualStudio.CSharpNavigator.Protocol;

public sealed class BridgeOperationStatusRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string RequestId { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public int MaxResults { get; set; } = 10;

    public bool IncludeResponse { get; set; }
}

public sealed class BridgeOperationStatus
{
    public string RequestId { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string PipeName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool CancellationRequested { get; set; }

    public bool IsTerminal { get; set; }

    public bool ResponseOmitted { get; set; }

    public string? ResponseJson { get; set; }
}

public static class BridgeOperationPolicy
{
    public static bool RequiresTracking(string method)
    {
        switch (method)
        {
            case "ApplyRename":
            case "ApplyCleanup":
            case "ApplyCodeFix":
            case "ApplyFixAll":
            case "ApplyTextEdit":
            case "StartDebugging":
            case "ContinueDebugging":
            case "BreakDebugging":
            case "StopDebugging":
            case "StepOver":
            case "StepInto":
            case "StepOut":
            case "SetDebugBreakpoint":
            case "RemoveDebugBreakpoint":
            case "EnableDebugBreakpoint":
            case "EvaluateDebugExpression":
            case "OpenSourceLocation":
            case "StartVisualStudioBuild":
                return true;
            default:
                return false;
        }
    }

    // Workspace mutations serialize with each other: two concurrent Apply calls
    // would both pass their safety checks and the later TryApplyChanges would
    // silently roll the other back. Debug controls and other tracked operations
    // intentionally stay concurrent.
    public static bool RequiresWorkspaceMutationSerialization(string method)
    {
        switch (method)
        {
            case "ApplyRename":
            case "ApplyCleanup":
            case "ApplyCodeFix":
            case "ApplyFixAll":
            case "ApplyTextEdit":
                return true;
            default:
                return false;
        }
    }
}
