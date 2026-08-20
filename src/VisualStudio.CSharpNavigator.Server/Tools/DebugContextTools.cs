using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class DebugContextTools
{
    private readonly CodeNavigationTools _inner;

    public DebugContextTools(CodeNavigationTools inner)
    {
        _inner = inner;
    }

    [McpServerTool(Name = "get_debugger_status", ReadOnly = true, Idempotent = true)]
    [Description("Return the current Visual Studio debugger state, current process/thread, and current stack frame when available.")]
    public Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatus(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetDebuggerStatus(targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "get_debug_call_stack", ReadOnly = true, Idempotent = true)]
    [Description("Return the Visual Studio debugger call stack for the current or specified thread. Requires break mode.")]
    public Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStack(
        [Description("Optional debugger thread id. When omitted, the current thread is used.")]
        int? threadId = null,
        [Description("Maximum stack frames to return.")]
        int maxFrames = 100,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetDebugCallStack(threadId, maxFrames, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "get_debug_stack_frame_variables", ReadOnly = true, Idempotent = true)]
    [Description("Return arguments and locals for a debugger stack frame. Requires break mode.")]
    public Task<WorkspaceQueryResult<DebugVariableInfo>> GetDebugStackFrameVariables(
        [Description("Optional frame id returned by get_debug_call_stack. When omitted, the current frame is used.")]
        string? frameId = null,
        [Description("Maximum children to include per expandable variable.")]
        int maxChildren = 50,
        [Description("Maximum string length for variable values.")]
        int maxStringLength = 500,
        [Description("Whether private data members should be included when expanded by the debugger.")]
        bool includePrivate = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetDebugStackFrameVariables(
            frameId,
            maxChildren,
            maxStringLength,
            includePrivate,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "evaluate_debug_expression", ReadOnly = false, Idempotent = false)]
    [Description("Evaluate an expression in the current Visual Studio debugger frame. Requires break mode; side effects must be explicitly allowed.")]
    public Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateDebugExpression(
        [Description("Expression to evaluate in the selected debugger stack frame.")]
        string expression,
        [Description("Optional frame id returned by get_debug_call_stack. When omitted, the current frame is used.")]
        string? frameId = null,
        [Description("Maximum children to include for expandable values.")]
        int maxChildren = 50,
        [Description("Maximum string length for expression values.")]
        int maxStringLength = 500,
        [Description("Whether evaluation may invoke property getters, ToString, or other debugger-side effects.")]
        bool allowSideEffects = false,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.EvaluateDebugExpression(
            expression,
            frameId,
            maxChildren,
            maxStringLength,
            allowSideEffects,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "batch_evaluate_debug_expressions", ReadOnly = false, Idempotent = false)]
    [Description("Evaluate up to 20 expressions in the same Visual Studio debugger frame with one MCP tool call. Requires break mode; side effects must be explicitly allowed.")]
    public Task<WorkspaceQueryResult<DebugExpressionResult>> BatchEvaluateDebugExpressions(
        [Description("Expressions to evaluate. Empty expressions are rejected; duplicates are evaluated once.")]
        string[] expressions,
        [Description("Optional frame id returned by get_debug_call_stack. When omitted, the current frame is used.")]
        string? frameId = null,
        [Description("Maximum children to include per expandable value.")]
        int maxChildren = 20,
        [Description("Maximum string length per expression value.")]
        int maxStringLength = 500,
        [Description("Whether evaluation may invoke debugger-side effects.")]
        bool allowSideEffects = false,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.BatchEvaluateDebugExpressions(
            expressions,
            frameId,
            maxChildren,
            maxStringLength,
            allowSideEffects,
            targetPipeName,
            targetInstanceId,
            targetSolutionPath,
            cancellationToken);
    }

    [McpServerTool(Name = "list_debug_threads", ReadOnly = true, Idempotent = true)]
    [Description("List Visual Studio debugger threads. Requires an active debugging session.")]
    public Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreads(
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListDebugThreads(targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }

    [McpServerTool(Name = "list_debug_breakpoints", ReadOnly = true, Idempotent = true)]
    [Description("List Visual Studio breakpoints without mutating debugger state.")]
    public Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListDebugBreakpoints(
        [Description("Maximum breakpoints to return.")]
        int maxResults = 500,
        [Description("Optional target Visual Studio bridge pipe name. Highest priority when provided.")]
        string? targetPipeName = null,
        [Description("Optional target Visual Studio bridge instance id.")]
        string? targetInstanceId = null,
        [Description("Optional target Visual Studio solution path.")]
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        return _inner.ListDebugBreakpoints(maxResults, targetPipeName, targetInstanceId, targetSolutionPath, cancellationToken);
    }
}
