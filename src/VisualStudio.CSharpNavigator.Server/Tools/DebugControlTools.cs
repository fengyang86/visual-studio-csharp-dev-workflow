using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class DebugControlTools
{
    private readonly CodeNavigationTools _inner;

    public DebugControlTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "start_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Start debugging in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StartDebugging(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.StartDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "continue_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Continue the paused debugger in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebugging(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.ContinueDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "break_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Break into a running debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebugging(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.BreakDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "stop_debugging", ReadOnly = false, Idempotent = false)]
    [Description("Stop the debugger session in the targeted Visual Studio instance. This mutates debugger state and requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StopDebugging(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.StopDebugging(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "step_over", ReadOnly = false, Idempotent = false)]
    [Description("Step over the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOver(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.StepOver(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "step_into", ReadOnly = false, Idempotent = false)]
    [Description("Step into from the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepInto(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.StepInto(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "step_out", ReadOnly = false, Idempotent = false)]
    [Description("Step out from the current debugger frame in the targeted Visual Studio instance. Requires break mode and an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> StepOut(string? targetPipeName = null, string? targetInstanceId = null, string? targetSolutionPath = null, int timeoutMilliseconds = 10000, CancellationToken cancellationToken = default)
    {
        return _inner.StepOut(targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "set_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Set a source breakpoint by absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpoint(
        string filePath,
        int line,
        int column = 1,
        string? condition = null,
        DebugBreakpointConditionMode conditionMode = DebugBreakpointConditionMode.WhenTrue,
        int hitCountTarget = 0,
        DebugBreakpointHitCountMode hitCountMode = DebugBreakpointHitCountMode.None,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return _inner.SetDebugBreakpoint(filePath, line, column, condition, conditionMode, hitCountTarget, hitCountMode, targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "remove_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Remove exactly one breakpoint by breakpoint name or absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpoint(
        string? breakpointName = null,
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return _inner.RemoveDebugBreakpoint(breakpointName, filePath, line, column, targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }

    [McpServerTool(Name = "enable_debug_breakpoint", ReadOnly = false, Idempotent = false)]
    [Description("Enable or disable exactly one breakpoint by breakpoint name or absolute file path and line in the targeted Visual Studio instance. Requires an explicit target.")]
    public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpoint(
        bool enabled,
        string? breakpointName = null,
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        int timeoutMilliseconds = 10000,
        CancellationToken cancellationToken = default)
    {
        return _inner.EnableDebugBreakpoint(enabled, breakpointName, filePath, line, column, targetPipeName, targetInstanceId, targetSolutionPath, timeoutMilliseconds, cancellationToken);
    }
}
