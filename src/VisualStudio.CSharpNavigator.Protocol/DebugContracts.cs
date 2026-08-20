using System;
using System.Collections.Generic;

namespace VisualStudio.CSharpNavigator.Protocol;

public enum DebuggerState
{
    Unknown = 0,
    Design = 1,
    Run = 2,
    Break = 3,
}

public enum DebugVariableCategory
{
    Unknown = 0,
    Argument = 1,
    Local = 2,
    DataMember = 3,
}

public enum DebugControlAction
{
    Unknown = 0,
    Start = 1,
    Continue = 2,
    Break = 3,
    Stop = 4,
    StepOver = 5,
    StepInto = 6,
    StepOut = 7,
    SetBreakpoint = 8,
    RemoveBreakpoint = 9,
    EnableBreakpoint = 10,
}

public enum DebugBreakpointConditionMode
{
    None = 0,
    WhenTrue = 1,
    WhenChanged = 2,
}

public enum DebugBreakpointHitCountMode
{
    None = 0,
    Equal = 1,
    GreaterOrEqual = 2,
    Multiple = 3,
}

public sealed class DebugSessionPreparationPlan
{
    public string Status { get; set; } = string.Empty;

    public NavigatorHealthReport Health { get; set; } = new();

    public DebugSessionStatus? DebuggerStatus { get; set; }

    public DebugBreakpointInfo[] Breakpoints { get; set; } = Array.Empty<DebugBreakpointInfo>();

    public DebugStackFrameInfo[] CallStack { get; set; } = Array.Empty<DebugStackFrameInfo>();

    public SourceContextSnippet[] SourceSnippets { get; set; } = Array.Empty<SourceContextSnippet>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpDebugScenarioPlan
{
    public string Status { get; set; } = string.Empty;

    public string ScenarioName { get; set; } = string.Empty;

    public string ProblemText { get; set; } = string.Empty;

    public DebugSessionPreparationPlan CurrentSession { get; set; } = new();

    public CSharpDebugScenarioStep[] Steps { get; set; } = Array.Empty<CSharpDebugScenarioStep>();

    public RecommendedNextAction[] RecommendedNextActions { get; set; } = Array.Empty<RecommendedNextAction>();

    public string[] SuggestedNextSteps { get; set; } = Array.Empty<string>();
}

public sealed class CSharpDebugScenarioStep
{
    public int Order { get; set; }

    public string Phase { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string ArgumentsSummary { get; set; } = string.Empty;

    public bool IsMutating { get; set; }

    public bool RequiresExplicitTarget { get; set; }

    public string StopIf { get; set; } = string.Empty;

    public string ExpectedEvidence { get; set; } = string.Empty;
}

public sealed class DebuggerStatusRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class DebugCallStackRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public int? ThreadId { get; set; }

    public int MaxFrames { get; set; } = 100;
}

public sealed class DebugStackFrameVariablesRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string? FrameId { get; set; }

    public int MaxChildren { get; set; } = 50;

    public int MaxStringLength { get; set; } = 500;

    public bool IncludePrivate { get; set; }
}

public sealed class DebugExpressionRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public string Expression { get; set; } = string.Empty;

    public string? FrameId { get; set; }

    public int MaxChildren { get; set; } = 50;

    public int MaxStringLength { get; set; } = 500;

    public bool AllowSideEffects { get; set; }
}

public sealed class DebugThreadsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }
}

public sealed class DebugBreakpointsRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public int MaxResults { get; set; } = 500;
}

public sealed class DebugControlRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public DebugControlAction Action { get; set; }

    public int TimeoutMilliseconds { get; set; } = 10000;
}

public sealed class DebugBreakpointMutationRequest : IVisualStudioBridgeTargetedRequest
{
    public VisualStudioBridgeTarget? Target { get; set; }

    public DebugControlAction Action { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Column { get; set; } = 1;

    public string BreakpointName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Condition { get; set; } = string.Empty;

    public DebugBreakpointConditionMode ConditionMode { get; set; } = DebugBreakpointConditionMode.WhenTrue;

    public int HitCountTarget { get; set; }

    public DebugBreakpointHitCountMode HitCountMode { get; set; }

    public int TimeoutMilliseconds { get; set; } = 10000;
}

public sealed class DebugProcessInfo
{
    public int ProcessId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TransportName { get; set; } = string.Empty;
}

public sealed class DebugThreadInfo
{
    public int ThreadId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public bool IsCurrent { get; set; }
}

public sealed class DebugStackFrameInfo
{
    public string FrameId { get; set; } = string.Empty;

    public int Index { get; set; }

    public int ThreadId { get; set; }

    public string FunctionName { get; set; } = string.Empty;

    public string ModuleName { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public SourceSpan? Span { get; set; }

    public bool IsCurrent { get; set; }
}

public sealed class DebugSessionStatus
{
    public DebuggerState State { get; set; }

    public bool IsDebugging { get; set; }

    public bool IsPaused { get; set; }

    public DebugProcessInfo? CurrentProcess { get; set; }

    public DebugThreadInfo? CurrentThread { get; set; }

    public DebugStackFrameInfo? CurrentFrame { get; set; }

    public DebugBreakpointInfo? CurrentBreakpoint { get; set; }
}

public sealed class DebugVariableInfo
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DebugVariableCategory Category { get; set; }

    public bool IsExpandable { get; set; }

    public bool IsSensitiveRedacted { get; set; }

    public string EvaluationError { get; set; } = string.Empty;

    public IReadOnlyList<DebugVariableInfo> Children { get; set; } = Array.Empty<DebugVariableInfo>();
}

public sealed class DebugExpressionResult
{
    public string Expression { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public bool SideEffectRisk { get; set; }

    public IReadOnlyList<DebugVariableInfo> Children { get; set; } = Array.Empty<DebugVariableInfo>();
}

public sealed class DebugBreakpointInfo
{
    public string Name { get; set; } = string.Empty;

    public string FunctionName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public string Condition { get; set; } = string.Empty;

    public DebugBreakpointConditionMode ConditionMode { get; set; }

    public int HitCountTarget { get; set; }

    public DebugBreakpointHitCountMode HitCountMode { get; set; }

    public int CurrentHitCount { get; set; }

    public SourceSpan? Span { get; set; }
}

public sealed class DebugControlResult
{
    public DebugControlAction Action { get; set; }

    public bool Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;

    public DebugSessionStatus? Status { get; set; }

    public DebugBreakpointInfo? Breakpoint { get; set; }

    public int AffectedBreakpointCount { get; set; }
}
