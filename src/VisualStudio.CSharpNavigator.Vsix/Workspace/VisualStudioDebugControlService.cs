using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;
using Microsoft.VisualStudio.Shell;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed class VisualStudioDebugControlService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly AsyncPackage _package;

    public VisualStudioDebugControlService(AsyncPackage package)
    {
        _package = package;
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StartDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.Start,
            expectedState: DebuggerState.Design,
            completion: state => state is DebuggerState.Run or DebuggerState.Break,
            successMessage: "Start debugging was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: start_debugging requires Visual Studio to be in design mode.",
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.Continue,
            expectedState: DebuggerState.Break,
            completion: state => state is DebuggerState.Run or DebuggerState.Design,
            successMessage: "Continue debugging was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: continue_debugging requires Visual Studio to be in break mode.",
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.Break,
            expectedState: DebuggerState.Run,
            completion: state => state is DebuggerState.Break or DebuggerState.Design,
            successMessage: "Break debugging was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: break_debugging requires Visual Studio to be in run mode.",
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StopDebuggingAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.Stop,
            expectedState: null,
            completion: state => state == DebuggerState.Design,
            successMessage: "Stop debugging was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: stop_debugging requires Visual Studio to be debugging.",
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepOverAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.StepOver,
            expectedState: DebuggerState.Break,
            completion: state => state is DebuggerState.Break or DebuggerState.Design,
            successMessage: "Step over was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: step_over requires Visual Studio to be in break mode.",
            cancellationToken,
            delayBeforePolling: true);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepIntoAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.StepInto,
            expectedState: DebuggerState.Break,
            completion: state => state is DebuggerState.Break or DebuggerState.Design,
            successMessage: "Step into was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: step_into requires Visual Studio to be in break mode.",
            cancellationToken,
            delayBeforePolling: true);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> StepOutAsync(
        DebugControlRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteDebuggerActionAsync(
            request,
            DebugControlAction.StepOut,
            expectedState: DebuggerState.Break,
            completion: state => state is DebuggerState.Break or DebuggerState.Design,
            successMessage: "Step out was submitted to Visual Studio.",
            preconditionFailure: "DebuggerStateInvalid: step_out requires Visual Studio to be in break mode.",
            cancellationToken,
            delayBeforePolling: true);
    }

    public async Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || request.Line <= 0)
        {
            return Failure("Breakpoint filePath and one-based line are required.", DebugControlAction.SetBreakpoint);
        }

        var optionsValidation = ValidateBreakpointOptions(request);
        if (optionsValidation is not null)
        {
            return Failure(optionsValidation, DebugControlAction.SetBreakpoint);
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugControlResult>();
        }

        var debugger = debuggerResult.Debugger!;
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var breakpoint = AddBreakpointOnMainThread(debugger, request);
            var status = CreateStatus(debugger);
            return ControlResult(
                DebugControlAction.SetBreakpoint,
                succeeded: breakpoint is not null,
                breakpoint is null
                    ? "Visual Studio accepted the breakpoint request but did not return a breakpoint descriptor."
                    : "Breakpoint was set in Visual Studio.",
                status,
                breakpoint is null ? null : CreateBreakpointInfo(breakpoint),
                breakpoint is null ? 0 : 1,
                new[] { "Debug Control mutated Visual Studio breakpoint state." },
                isPartial: breakpoint is null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure("SetBreakpointFailed: " + ex.Message, DebugControlAction.SetBreakpoint);
        }
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        return MutateExistingBreakpointAsync(
            request,
            DebugControlAction.RemoveBreakpoint,
            "Breakpoint was removed from Visual Studio.",
            cancellationToken);
    }

    public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpointAsync(
        DebugBreakpointMutationRequest request,
        CancellationToken cancellationToken)
    {
        return MutateExistingBreakpointAsync(
            request,
            DebugControlAction.EnableBreakpoint,
            request.Enabled ? "Breakpoint was enabled in Visual Studio." : "Breakpoint was disabled in Visual Studio.",
            cancellationToken);
    }

    private async Task<WorkspaceQueryResult<DebugControlResult>> ExecuteDebuggerActionAsync(
        DebugControlRequest request,
        DebugControlAction action,
        DebuggerState? expectedState,
        Func<DebuggerState, bool> completion,
        string successMessage,
        string preconditionFailure,
        CancellationToken cancellationToken,
        bool delayBeforePolling = false)
    {
        if (request.Action != action)
        {
            return Failure($"Debug control request action mismatch. Expected {action}, got {request.Action}.", action);
        }

        var timeoutValidation = ValidateTimeout(request.TimeoutMilliseconds);
        if (timeoutValidation is not null)
        {
            return Failure(timeoutValidation, action);
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugControlResult>();
        }

        var debugger = debuggerResult.Debugger!;
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var before = CreateStatus(debugger);
        if (DebugControlSemantics.TryGetAlreadySatisfiedMessage(action, before, out var alreadySatisfiedMessage))
        {
            return ControlResult(
                action,
                succeeded: true,
                alreadySatisfiedMessage,
                before,
                breakpoint: null,
                affectedBreakpointCount: 0,
                diagnostics: new[] { DebugControlSemantics.AlreadySatisfiedDiagnostic },
                isPartial: false);
        }

        if (expectedState.HasValue)
        {
            if (before.State != expectedState.Value)
            {
                return ControlResult(
                    action,
                    succeeded: false,
                    preconditionFailure,
                    before,
                    breakpoint: null,
                    affectedBreakpointCount: 0,
                    diagnostics: new[] { preconditionFailure },
                    isPartial: true);
            }
        }
        else if (!before.IsDebugging)
        {
            return ControlResult(
                action,
                succeeded: false,
                preconditionFailure,
                before,
                breakpoint: null,
                affectedBreakpointCount: 0,
                diagnostics: new[] { preconditionFailure },
                isPartial: true);
        }

        try
        {
            InvokeDebuggerActionOnMainThread(debugger, action);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ControlResult(
                action,
                succeeded: false,
                $"{action} failed in Visual Studio: {ex.Message}",
                CreateStatus(debugger),
                breakpoint: null,
                affectedBreakpointCount: 0,
                diagnostics: new[] { $"{action} failed in Visual Studio: {ex.Message}" },
                isPartial: true);
        }

        if (delayBeforePolling && request.TimeoutMilliseconds > 0)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        var waitResult = await WaitForStateAsync(
            debugger,
            completion,
            request.TimeoutMilliseconds,
            cancellationToken).ConfigureAwait(false);
        var diagnostics = waitResult.Completed
            ? new[] { "Debug Control mutated Visual Studio debugger state." }
            : new[]
            {
                "Debug Control mutated Visual Studio debugger state.",
                $"{action} did not reach the expected debugger state within TimeoutMilliseconds={request.TimeoutMilliseconds}.",
            };

        return ControlResult(
            action,
            succeeded: true,
            waitResult.Completed ? successMessage : $"{successMessage} State transition is still pending.",
            waitResult.Status,
            breakpoint: null,
            affectedBreakpointCount: 0,
            diagnostics,
            isPartial: !waitResult.Completed);
    }

    private async Task<WorkspaceQueryResult<DebugControlResult>> MutateExistingBreakpointAsync(
        DebugBreakpointMutationRequest request,
        DebugControlAction action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (request.Action != action)
        {
            return Failure($"Debug breakpoint request action mismatch. Expected {action}, got {request.Action}.", action);
        }

        var timeoutValidation = ValidateTimeout(request.TimeoutMilliseconds);
        if (timeoutValidation is not null)
        {
            return Failure(timeoutValidation, action);
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugControlResult>();
        }

        var debugger = debuggerResult.Debugger!;
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var matches = FindBreakpoints(debugger, request.BreakpointName, request.FilePath, request.Line, request.Column).ToArray();
        if (matches.Length == 0)
        {
            return ControlResult(
                action,
                succeeded: false,
                "BreakpointNotFound: no breakpoint matched the requested selector.",
                CreateStatus(debugger),
                breakpoint: null,
                affectedBreakpointCount: 0,
                diagnostics: new[] { "BreakpointNotFound: no breakpoint matched the requested selector." },
                isPartial: true);
        }

        if (matches.Length > 1)
        {
            return ControlResult(
                action,
                succeeded: false,
                "AmbiguousBreakpoint: multiple breakpoints matched the requested selector. Pass breakpointName or a more specific source position.",
                CreateStatus(debugger),
                breakpoint: null,
                affectedBreakpointCount: matches.Length,
                diagnostics: new[] { "AmbiguousBreakpoint: multiple breakpoints matched the requested selector. Pass breakpointName or a more specific source position." },
                isPartial: true);
        }

        var selected = matches[0];
        DebugBreakpointInfo? breakpointInfo = null;
        try
        {
            MutateBreakpointOnMainThread(selected, request, action);
            if (action != DebugControlAction.RemoveBreakpoint)
            {
                breakpointInfo = CreateBreakpointInfo(selected);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ControlResult(
                action,
                succeeded: false,
                $"{action} failed in Visual Studio: {ex.Message}",
                CreateStatus(debugger),
                breakpoint: null,
                affectedBreakpointCount: 0,
                diagnostics: new[] { $"{action} failed in Visual Studio: {ex.Message}" },
                isPartial: true);
        }

        return ControlResult(
            action,
            succeeded: true,
            successMessage,
            CreateStatus(debugger),
            breakpointInfo,
            affectedBreakpointCount: 1,
            diagnostics: new[] { "Debug Control mutated Visual Studio breakpoint state." },
            isPartial: false);
    }

    private async Task<DebuggerLookupResult> TryGetDebuggerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            return CreateDebuggerLookupResultOnMainThread(dte);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DebuggerLookupResult.FromFailure("DebuggerUnavailable: failed to obtain Visual Studio debugger: " + ex.Message);
        }
    }

    private async Task<(DebugSessionStatus Status, bool Completed)> WaitForStateAsync(
        EnvDTE.Debugger debugger,
        Func<DebuggerState, bool> completion,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var status = CreateStatus(debugger);
        if (timeoutMilliseconds == 0 || completion(status.State))
        {
            return (status, completion(status.State));
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            status = CreateStatus(debugger);
            if (completion(status.State))
            {
                return (status, true);
            }
        }

        return (status, false);
    }

    private static DebuggerLookupResult CreateDebuggerLookupResultOnMainThread(object? dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte is null)
        {
            return DebuggerLookupResult.FromFailure("DebuggerUnavailable: EnvDTE service is not available.");
        }

        var debugger = ((EnvDTE.DTE)dte).Debugger;
        return debugger is null
            ? DebuggerLookupResult.FromFailure("DebuggerUnavailable: EnvDTE.Debugger is not available.")
            : DebuggerLookupResult.Success(debugger);
    }

    private static void InvokeDebuggerActionOnMainThread(
        EnvDTE.Debugger debugger,
        DebugControlAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (action)
        {
            case DebugControlAction.Start:
            case DebugControlAction.Continue:
                debugger.Go(false);
                break;
            case DebugControlAction.Break:
                debugger.Break(false);
                break;
            case DebugControlAction.Stop:
                debugger.Stop(false);
                break;
            case DebugControlAction.StepOver:
                debugger.StepOver(false);
                break;
            case DebugControlAction.StepInto:
                debugger.StepInto(false);
                break;
            case DebugControlAction.StepOut:
                debugger.StepOut(false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported debug control action: {action}.");
        }
    }

    private static EnvDTE.Breakpoint? AddBreakpointOnMainThread(
        EnvDTE.Debugger debugger,
        DebugBreakpointMutationRequest request)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var breakpoints = debugger.Breakpoints.Add(
            string.Empty,
            request.FilePath,
            request.Line,
            request.Column <= 0 ? 1 : request.Column,
            request.Condition,
            ToEnvDteConditionType(request.ConditionMode),
            "C#",
            string.Empty,
            0,
            string.Empty,
            request.HitCountTarget,
            ToEnvDteHitCountType(request.HitCountMode));

        return EnumerateBreakpoints(breakpoints).FirstOrDefault()
            ?? FindBreakpoints(debugger, request.BreakpointName, request.FilePath, request.Line, request.Column).FirstOrDefault();
    }

    private static void MutateBreakpointOnMainThread(
        EnvDTE.Breakpoint breakpoint,
        DebugBreakpointMutationRequest request,
        DebugControlAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (action)
        {
            case DebugControlAction.RemoveBreakpoint:
                breakpoint.Delete();
                break;
            case DebugControlAction.EnableBreakpoint:
                breakpoint.Enabled = request.Enabled;
                break;
            default:
                throw new InvalidOperationException($"Unsupported breakpoint mutation action: {action}.");
        }
    }

    private static DebugSessionStatus CreateStatus(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var state = ReadDebuggerState(debugger);
        var status = new DebugSessionStatus
        {
            State = state,
            IsDebugging = state is DebuggerState.Run or DebuggerState.Break,
            IsPaused = state == DebuggerState.Break,
        };

        if (status.IsDebugging)
        {
            status.CurrentProcess = CreateCurrentProcessInfo(debugger);
            status.CurrentThread = CreateCurrentThreadInfo(debugger, isCurrent: true);
        }

        if (status.IsPaused)
        {
            var threadId = status.CurrentThread?.ThreadId ?? 0;
            status.CurrentFrame = CreateCurrentStackFrameInfo(debugger, threadId);
            status.CurrentBreakpoint = CreateLastHitBreakpointInfo(debugger);
        }

        return status;
    }

    private static DebuggerState ReadDebuggerState(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        EnvDTE.dbgDebugMode mode;
        try
        {
            mode = debugger.CurrentMode;
        }
        catch
        {
            mode = EnvDTE.dbgDebugMode.dbgDesignMode;
        }

        return mode switch
        {
            EnvDTE.dbgDebugMode.dbgBreakMode => DebuggerState.Break,
            EnvDTE.dbgDebugMode.dbgRunMode => DebuggerState.Run,
            EnvDTE.dbgDebugMode.dbgDesignMode => DebuggerState.Design,
            _ => DebuggerState.Unknown,
        };
    }

    private static DebugProcessInfo? CreateCurrentProcessInfo(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return CreateProcessInfo(debugger.CurrentProcess);
        }
        catch
        {
            return null;
        }
    }

    private static DebugThreadInfo? CreateCurrentThreadInfo(EnvDTE.Debugger debugger, bool isCurrent)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return CreateThreadInfo(debugger.CurrentThread, isCurrent);
        }
        catch
        {
            return null;
        }
    }

    private static DebugStackFrameInfo? CreateCurrentStackFrameInfo(
        EnvDTE.Debugger debugger,
        int threadId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var currentFrame = debugger.CurrentStackFrame;
            var currentThread = debugger.CurrentThread;
            var index = 0;
            foreach (var frame in EnumerateStackFrames(currentThread))
            {
                if (IsSameStackFrame(frame, currentFrame))
                {
                    return CreateStackFrameInfo(frame, threadId, index, isCurrent: true);
                }

                index++;
            }

            return CreateStackFrameInfo(currentFrame, threadId, index: 0, isCurrent: true);
        }
        catch
        {
            return null;
        }
    }

    private static DebugBreakpointInfo? CreateLastHitBreakpointInfo(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var breakpoint = ReadDynamic<object?>(() => ((dynamic)debugger).BreakpointLastHit, null);
        return breakpoint is EnvDTE.Breakpoint lastHit ? CreateBreakpointInfo(lastHit) : null;
    }

    private static DebugProcessInfo? CreateProcessInfo(dynamic? process)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (process is null)
        {
            return null;
        }

        return new DebugProcessInfo
        {
            ProcessId = ReadDynamic(() => (int)process.ProcessID, 0),
            Name = ReadDynamic(() => (string)process.Name, string.Empty),
            TransportName = ReadDynamic(() => (string)process.Transport.Name, string.Empty),
        };
    }

    private static DebugThreadInfo? CreateThreadInfo(dynamic? thread, bool isCurrent)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (thread is null)
        {
            return null;
        }

        return new DebugThreadInfo
        {
            ThreadId = ReadDynamic(() => (int)thread.ID, 0),
            Name = ReadDynamic(() => (string)thread.Name, string.Empty),
            Category = ReadDynamic(() => (string)thread.Category, string.Empty),
            State = ReadDynamic(() => Convert.ToString(thread.State) ?? string.Empty, string.Empty),
            IsCurrent = isCurrent,
        };
    }

    private static DebugStackFrameInfo? CreateStackFrameInfo(
        dynamic? frame,
        int threadId,
        int index,
        bool isCurrent)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (frame is null)
        {
            return null;
        }

        var fileName = ReadDynamic(() => (string)frame.FileName, string.Empty);
        var line = ReadDynamic(() => (int)frame.LineNumber, 0);
        return new DebugStackFrameInfo
        {
            FrameId = $"{threadId}:{index}",
            ThreadId = threadId,
            Index = index,
            FunctionName = ReadDynamic(() => (string)frame.FunctionName, string.Empty),
            ModuleName = ReadDynamic(() => (string)frame.Module, string.Empty),
            Language = ReadDynamic(() => (string)frame.Language, string.Empty),
            Span = !string.IsNullOrWhiteSpace(fileName) && line > 0
                ? new SourceSpan
                {
                    FilePath = fileName,
                    StartLine = line,
                    StartColumn = 1,
                    EndLine = line,
                    EndColumn = 1,
                }
                : null,
            IsCurrent = isCurrent,
        };
    }

    private static IReadOnlyList<dynamic> EnumerateStackFrames(dynamic? thread)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (thread is null)
        {
            return Array.Empty<dynamic>();
        }

        return EnumerateDynamic(ReadDynamic(() => thread.StackFrames, null));
    }

    private static bool IsSameStackFrame(dynamic? candidate, dynamic? current)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (candidate is null || current is null)
        {
            return false;
        }

        var candidateFunction = ReadDynamic(() => (string)candidate.FunctionName, string.Empty);
        var currentFunction = ReadDynamic(() => (string)current.FunctionName, string.Empty);
        if (!string.Equals(candidateFunction, currentFunction, StringComparison.Ordinal))
        {
            return false;
        }

        var candidateModule = ReadDynamic(() => (string)candidate.Module, string.Empty);
        var currentModule = ReadDynamic(() => (string)current.Module, string.Empty);
        if (!string.Equals(candidateModule, currentModule, StringComparison.Ordinal))
        {
            return false;
        }

        var candidateFile = ReadDynamic(() => (string)candidate.FileName, string.Empty);
        var currentFile = ReadDynamic(() => (string)current.FileName, string.Empty);
        if (!string.Equals(candidateFile, currentFile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateLine = ReadDynamic(() => (int)candidate.LineNumber, 0);
        var currentLine = ReadDynamic(() => (int)current.LineNumber, 0);
        return candidateLine == currentLine;
    }

    private static IReadOnlyList<EnvDTE.Breakpoint> FindBreakpoints(
        EnvDTE.Debugger debugger,
        string? breakpointName,
        string? filePath,
        int line,
        int column)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var breakpoints = EnumerateBreakpoints(ReadBreakpointCollection(debugger));
        if (!string.IsNullOrWhiteSpace(breakpointName))
        {
            var namedMatches = new List<EnvDTE.Breakpoint>();
            foreach (var breakpoint in breakpoints)
            {
                if (string.Equals(ReadBreakpointName(breakpoint), breakpointName, StringComparison.Ordinal))
                {
                    namedMatches.Add(breakpoint);
                }
            }

            return namedMatches;
        }

        if (string.IsNullOrWhiteSpace(filePath) || line <= 0)
        {
            return Array.Empty<EnvDTE.Breakpoint>();
        }

        var expectedPath = NormalizePath(filePath);
        var sourceMatches = new List<EnvDTE.Breakpoint>();
        foreach (var breakpoint in breakpoints)
        {
            var breakpointPath = NormalizePath(ReadBreakpointFile(breakpoint));
            var breakpointLine = ReadBreakpointFileLine(breakpoint);
            var breakpointColumn = ReadBreakpointFileColumn(breakpoint);
            if (string.Equals(breakpointPath, expectedPath, StringComparison.OrdinalIgnoreCase)
                && breakpointLine == line
                && (column <= 0 || breakpointColumn == column))
            {
                sourceMatches.Add(breakpoint);
            }
        }

        return sourceMatches;
    }

    private static dynamic? ReadBreakpointCollection(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return debugger.Breakpoints;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<EnvDTE.Breakpoint> EnumerateBreakpoints(dynamic? collection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var items = new List<EnvDTE.Breakpoint>();
        if (collection is null)
        {
            return items;
        }

        try
        {
            foreach (var item in collection)
            {
                if (item is EnvDTE.Breakpoint breakpoint)
                {
                    items.Add(breakpoint);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Failed to enumerate Visual Studio breakpoints: " + ex.Message);
        }

        return items;
    }

    private static IReadOnlyList<dynamic> EnumerateDynamic(dynamic? collection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var items = new List<dynamic>();
        if (collection is null)
        {
            return items;
        }

        try
        {
            foreach (var item in collection)
            {
                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Failed to enumerate Visual Studio debugger collection: " + ex.Message);
        }

        return items;
    }

    private static DebugBreakpointInfo CreateBreakpointInfo(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var file = ReadBreakpointFile(breakpoint);
        var line = ReadBreakpointFileLine(breakpoint);
        var column = Math.Max(ReadBreakpointFileColumn(breakpoint), 1);
        var condition = ReadBreakpointCondition(breakpoint);
        var hitCountMode = ReadBreakpointHitCountMode(breakpoint);
        return new DebugBreakpointInfo
        {
            Name = ReadBreakpointName(breakpoint),
            FunctionName = ReadBreakpointFunctionName(breakpoint),
            IsEnabled = ReadBreakpointEnabled(breakpoint),
            Condition = condition,
            ConditionMode = ReadBreakpointConditionMode(breakpoint, condition),
            HitCountTarget = hitCountMode == DebugBreakpointHitCountMode.None
                ? 0
                : ReadBreakpointHitCountTarget(breakpoint),
            HitCountMode = hitCountMode,
            CurrentHitCount = ReadBreakpointCurrentHitCount(breakpoint),
            Span = !string.IsNullOrWhiteSpace(file) && line > 0
                ? new SourceSpan
                {
                    FilePath = file,
                    StartLine = line,
                    StartColumn = column,
                    EndLine = line,
                    EndColumn = column,
                }
                : null,
        };
    }

    private static string ReadBreakpointName(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadBreakpointFunctionName(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.FunctionName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadBreakpointFile(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.File ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ReadBreakpointFileLine(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.FileLine;
        }
        catch
        {
            return 0;
        }
    }

    private static int ReadBreakpointFileColumn(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.FileColumn;
        }
        catch
        {
            return 0;
        }
    }

    private static bool ReadBreakpointEnabled(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadBreakpointCondition(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.Condition ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DebugBreakpointConditionMode ReadBreakpointConditionMode(EnvDTE.Breakpoint breakpoint, string condition)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrWhiteSpace(condition))
        {
            return DebugBreakpointConditionMode.None;
        }

        try
        {
            return FromEnvDteConditionType(breakpoint.ConditionType);
        }
        catch
        {
            return DebugBreakpointConditionMode.None;
        }
    }

    private static int ReadBreakpointHitCountTarget(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.HitCountTarget;
        }
        catch
        {
            return 0;
        }
    }

    private static DebugBreakpointHitCountMode ReadBreakpointHitCountMode(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return FromEnvDteHitCountType(breakpoint.HitCountType);
        }
        catch
        {
            return DebugBreakpointHitCountMode.None;
        }
    }

    private static int ReadBreakpointCurrentHitCount(EnvDTE.Breakpoint breakpoint)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            return breakpoint.CurrentHits;
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path ?? string.Empty;
        }
    }

    private static string? ValidateTimeout(int timeoutMilliseconds)
    {
        return timeoutMilliseconds is < 0 or > 60000
            ? "TimeoutMilliseconds must be between 0 and 60000."
            : null;
    }

    private static string? ValidateBreakpointOptions(DebugBreakpointMutationRequest request)
    {
        if (!Enum.IsDefined(typeof(DebugBreakpointConditionMode), request.ConditionMode))
        {
            return "Breakpoint conditionMode is not supported.";
        }

        if (!Enum.IsDefined(typeof(DebugBreakpointHitCountMode), request.HitCountMode))
        {
            return "Breakpoint hitCountMode is not supported.";
        }

        if (!string.IsNullOrWhiteSpace(request.Condition) && request.ConditionMode == DebugBreakpointConditionMode.None)
        {
            return "Breakpoint conditionMode must be WhenTrue or WhenChanged when condition is provided.";
        }

        if (string.IsNullOrWhiteSpace(request.Condition) && request.ConditionMode == DebugBreakpointConditionMode.WhenChanged)
        {
            return "Breakpoint condition is required when conditionMode is WhenChanged.";
        }

        if (request.HitCountTarget < 0)
        {
            return "Breakpoint hitCountTarget cannot be negative.";
        }

        if (request.HitCountMode == DebugBreakpointHitCountMode.None)
        {
            return request.HitCountTarget == 0
                ? null
                : "Breakpoint hitCountTarget requires hitCountMode other than None.";
        }

        return request.HitCountTarget > 0
            ? null
            : "Breakpoint hitCountTarget must be a positive integer when hitCountMode is not None.";
    }

    private static EnvDTE.dbgBreakpointConditionType ToEnvDteConditionType(DebugBreakpointConditionMode mode)
    {
        return mode == DebugBreakpointConditionMode.WhenChanged
            ? EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
            : EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue;
    }

    private static DebugBreakpointConditionMode FromEnvDteConditionType(EnvDTE.dbgBreakpointConditionType mode)
    {
        return mode == EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
            ? DebugBreakpointConditionMode.WhenChanged
            : DebugBreakpointConditionMode.WhenTrue;
    }

    private static EnvDTE.dbgHitCountType ToEnvDteHitCountType(DebugBreakpointHitCountMode mode)
    {
        return mode switch
        {
            DebugBreakpointHitCountMode.Equal => EnvDTE.dbgHitCountType.dbgHitCountTypeEqual,
            DebugBreakpointHitCountMode.GreaterOrEqual => EnvDTE.dbgHitCountType.dbgHitCountTypeGreaterOrEqual,
            DebugBreakpointHitCountMode.Multiple => EnvDTE.dbgHitCountType.dbgHitCountTypeMultiple,
            _ => EnvDTE.dbgHitCountType.dbgHitCountTypeNone,
        };
    }

    private static DebugBreakpointHitCountMode FromEnvDteHitCountType(EnvDTE.dbgHitCountType mode)
    {
        return mode switch
        {
            EnvDTE.dbgHitCountType.dbgHitCountTypeEqual => DebugBreakpointHitCountMode.Equal,
            EnvDTE.dbgHitCountType.dbgHitCountTypeGreaterOrEqual => DebugBreakpointHitCountMode.GreaterOrEqual,
            EnvDTE.dbgHitCountType.dbgHitCountTypeMultiple => DebugBreakpointHitCountMode.Multiple,
            _ => DebugBreakpointHitCountMode.None,
        };
    }

    private static WorkspaceQueryResult<DebugControlResult> ControlResult(
        DebugControlAction action,
        bool succeeded,
        string message,
        DebugSessionStatus status,
        DebugBreakpointInfo? breakpoint,
        int affectedBreakpointCount,
        IReadOnlyList<string> diagnostics,
        bool isPartial)
    {
        return new WorkspaceQueryResult<DebugControlResult>
        {
            Items = new[]
            {
                new DebugControlResult
                {
                    Action = action,
                    Succeeded = succeeded,
                    Message = message,
                    Status = status,
                    Breakpoint = breakpoint,
                    AffectedBreakpointCount = affectedBreakpointCount,
                },
            },
            Diagnostics = diagnostics,
            IsPartial = isPartial,
        };
    }

    private static WorkspaceQueryResult<DebugControlResult> Failure(
        string diagnostic,
        DebugControlAction action)
    {
        return new WorkspaceQueryResult<DebugControlResult>
        {
            Items = new[]
            {
                new DebugControlResult
                {
                    Action = action,
                    Succeeded = false,
                    Message = diagnostic,
                },
            },
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }

    private static T ReadDynamic<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class DebuggerLookupResult
    {
        private DebuggerLookupResult(EnvDTE.Debugger? debugger, QueryFailure? failure)
        {
            Debugger = debugger;
            Failure = failure;
        }

        public EnvDTE.Debugger? Debugger { get; }

        public QueryFailure? Failure { get; }

        public static DebuggerLookupResult Success(EnvDTE.Debugger debugger) => new(debugger, null);

        public static DebuggerLookupResult FromFailure(string diagnostic) => new(null, new QueryFailure(diagnostic));
    }

    private sealed class QueryFailure
    {
        public QueryFailure(string diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public string Diagnostic { get; }

        public WorkspaceQueryResult<T> As<T>() => new()
        {
            Items = Array.Empty<T>(),
            Diagnostics = new[] { Diagnostic },
            IsPartial = true,
        };
    }
}
