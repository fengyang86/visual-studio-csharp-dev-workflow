using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class DebugControlTools
{
    private const string TargetPipeNameDescription = "REQUIRED: target Visual Studio bridge pipe name. Debug control refuses to run without an explicit target.";
    private const string TargetInstanceIdDescription = "REQUIRED (if no pipe name): target Visual Studio bridge instance id from list_visual_studio_instances.";
    private const string TargetSolutionPathDescription = "REQUIRED (if no pipe/instance id): target Visual Studio solution path. Fails with candidates when ambiguous.";
    private const string TimeoutDescription = "How long to wait for the debugger action to complete, in milliseconds (1000-60000).";

    private readonly CodeNavigationTools _inner;

    public DebugControlTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "start_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Start debugging in the targeted Visual Studio instance using its startup project and active configuration. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StartDebugging(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.StartDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "continue_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Continue the paused debugger in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebugging(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.ContinueDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "break_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Break into a running debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebugging(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.BreakDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "stop_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Stop the debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StopDebugging(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.StopDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "step_over", ReadOnly = false, Idempotent = false)]
    [Description("Step over the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOver(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.StepOver(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "step_into", ReadOnly = false, Idempotent = false)]
    [Description("Step into from the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepInto(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.StepInto(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "step_out", ReadOnly = false, Idempotent = false)]
    [Description("Step out of the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOut(
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        [Description(TimeoutDescription)] int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
        => _inner.StepOut(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);

    [McpServerTool(Name = "set_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Set a source breakpoint at a file/line in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpoint(
        [Description("Absolute path of the source file containing the breakpoint.")] string filePath,
        [Description("One-based line number for the breakpoint.")] int line,
        [Description("One-based column; defaults to 1 when omitted.")] int column = 1,
        [Description("Optional condition expression; the breakpoint only hits when true.")] string? condition = null,
        [Description("Hit count target; use with hitCountMode other than None.")] int hitCountTarget = 0,
        [Description("Hit count mode: None, Equal, GreaterOrEqual, or Multiple.")] DebugBreakpointHitCountMode hitCountMode = DebugBreakpointHitCountMode.None,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.SetDebugBreakpoint(filePath, line, column, condition, condition is null ? DebugBreakpointConditionMode.None : DebugBreakpointConditionMode.WhenTrue, hitCountTarget, hitCountMode, targetPipeName, targetInstanceId, targetSolutionPath, 10000, cancellationToken);

    [McpServerTool(Name = "remove_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Remove a breakpoint by name or file/line in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpoint(
        [Description("Breakpoint name from list_debug_breakpoints, if known.")] string? breakpointName = null,
        [Description("Absolute path of the source file (alternative to breakpointName).")] string? filePath = null,
        [Description("One-based line number (used with filePath).")] int? line = null,
        [Description("One-based column; 0 matches any breakpoint on the line.")] int? column = null,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.RemoveDebugBreakpoint(breakpointName, filePath, line, column, targetPipeName, targetInstanceId, targetSolutionPath, 10000, cancellationToken);

    [McpServerTool(Name = "enable_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Enable or disable a breakpoint by name or file/line in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpoint(
        [Description("true to enable the breakpoint, false to disable it.")] bool enabled,
        [Description("Breakpoint name from list_debug_breakpoints, if known.")] string? breakpointName = null,
        [Description("Absolute path of the source file (alternative to breakpointName).")] string? filePath = null,
        [Description("One-based line number (used with filePath).")] int? line = null,
        [Description("One-based column (used with filePath).")] int? column = null,
        [Description(TargetPipeNameDescription)] string? targetPipeName = null,
        [Description(TargetInstanceIdDescription)] string? targetInstanceId = null,
        [Description(TargetSolutionPathDescription)] string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
        => _inner.EnableDebugBreakpoint(enabled, breakpointName, filePath, line, column, targetPipeName, targetInstanceId, targetSolutionPath, 10000, cancellationToken);
}
