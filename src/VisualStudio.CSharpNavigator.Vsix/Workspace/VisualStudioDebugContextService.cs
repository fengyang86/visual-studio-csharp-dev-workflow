using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisualStudio.CSharpNavigator.Protocol;
using Microsoft.VisualStudio.Shell;

namespace VisualStudio.CSharpNavigator.Vsix.Workspace;

internal sealed class VisualStudioDebugContextService
{
    private readonly AsyncPackage _package;

    public VisualStudioDebugContextService(AsyncPackage package)
    {
        _package = package;
    }

    public async Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatusAsync(
        DebuggerStatusRequest request,
        CancellationToken cancellationToken)
    {
        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugSessionStatus>();
        }

        dynamic debugger = debuggerResult.Debugger!;
        var state = ReadDebuggerState(debugger);
        var status = new DebugSessionStatus
        {
            State = state,
            IsDebugging = state is DebuggerState.Run or DebuggerState.Break,
            IsPaused = state == DebuggerState.Break,
        };

        if (status.IsDebugging)
        {
            status.CurrentProcess = CreateProcessInfo(ReadDynamic(() => debugger.CurrentProcess, null));
            status.CurrentThread = CreateThreadInfo(ReadDynamic(() => debugger.CurrentThread, null), isCurrent: true);
        }

        if (status.IsPaused)
        {
            var threadId = status.CurrentThread?.ThreadId ?? 0;
            status.CurrentFrame = CreateCurrentStackFrameInfo(debugger, threadId);
            var lastHitBreakpoint = ReadDynamic<object?>(() => debugger.BreakpointLastHit, null);
            status.CurrentBreakpoint = lastHitBreakpoint is null ? null : CreateBreakpointInfo(lastHitBreakpoint);
        }

        return Success(new[] { status });
    }

    public async Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreadsAsync(
        DebugThreadsRequest request,
        CancellationToken cancellationToken)
    {
        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugThreadInfo>();
        }

        dynamic debugger = debuggerResult.Debugger!;
        var state = ReadDebuggerState(debugger);
        if (state == DebuggerState.Design)
        {
            return Failure<DebugThreadInfo>("DebuggerNotActive: Visual Studio is not currently debugging.");
        }

        var currentThread = ReadDynamic<object?>(() => debugger.CurrentThread, null);
        var currentThreadId = currentThread is null
            ? 0
            : ReadDynamic(() => (int)((dynamic)currentThread).ID, 0);
        var threads = new List<DebugThreadInfo>();
        foreach (dynamic thread in EnumerateThreads(debugger))
        {
            var threadId = ReadDynamic(() => (int)thread.ID, 0);
            var threadInfo = CreateThreadInfo(thread, threadId == currentThreadId);
            if (threadInfo is not null)
            {
                threads.Add(threadInfo);
            }
        }

        var hasCurrentThread = threads.Any(thread => thread.ThreadId == currentThreadId && currentThreadId != 0);
        if (!hasCurrentThread && currentThread is not null)
        {
            var currentThreadInfo = CreateThreadInfo(currentThread, isCurrent: true);
            if (currentThreadInfo is not null)
            {
                threads.Insert(0, currentThreadInfo);
                return Success(
                    threads.ToArray(),
                    new[] { "EnvDTE CurrentProcess.Threads did not enumerate the current thread; returned CurrentThread as a fallback." },
                    isPartial: threads.Count == 1);
            }
        }

        return Success(threads.ToArray());
    }

    public async Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStackAsync(
        DebugCallStackRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxFrames is < 1 or > 500)
        {
            return Failure<DebugStackFrameInfo>("MaxFrames must be between 1 and 500.");
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugStackFrameInfo>();
        }

        dynamic debugger = debuggerResult.Debugger!;
        if (ReadDebuggerState(debugger) != DebuggerState.Break)
        {
            return Failure<DebugStackFrameInfo>("DebuggerNotPaused: call stack is available only when Visual Studio is in break mode.");
        }

        var thread = FindThread(debugger, request.ThreadId);
        if (thread is null)
        {
            return Failure<DebugStackFrameInfo>("ThreadNotFound: the requested debugger thread was not found.");
        }

        var threadId = ReadDynamic(() => (int)thread.ID, 0);
        var currentThreadId = ReadDynamic(() => (int)debugger.CurrentThread.ID, 0);
        var currentFrame = ReadDynamic(() => debugger.CurrentStackFrame, null);
        var currentFrameMarked = false;
        var frames = new List<DebugStackFrameInfo>();
        foreach (var frame in EnumerateStackFrames(thread))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = frames.Count;
            var isCurrent = !currentFrameMarked
                && threadId == currentThreadId
                && IsSameStackFrame(frame, currentFrame);
            if (isCurrent)
            {
                currentFrameMarked = true;
            }

            var info = CreateStackFrameInfo(
                frame,
                threadId,
                index,
                isCurrent);
            if (info is not null)
            {
                frames.Add(info);
            }

            if (frames.Count >= request.MaxFrames)
            {
                return Success(frames.ToArray(), new[] { $"Call stack was truncated at MaxFrames={request.MaxFrames}." }, isPartial: true);
            }
        }

        return Success(frames.ToArray());
    }

    public async Task<WorkspaceQueryResult<DebugVariableInfo>> GetStackFrameVariablesAsync(
        DebugStackFrameVariablesRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateValueLimits(request.MaxChildren, request.MaxStringLength);
        if (validation is not null)
        {
            return Failure<DebugVariableInfo>(validation);
        }

        var frameResult = await ResolveFrameAsync(request.FrameId, cancellationToken).ConfigureAwait(false);
        if (frameResult.Failure is not null)
        {
            return frameResult.Failure.As<DebugVariableInfo>();
        }

        dynamic frame = frameResult.Frame!;
        var variables = new List<DebugVariableInfo>();
        var diagnostics = new List<string>();
        var argumentNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in CreateVariables(
                     ReadDynamic(() => frame.Arguments, null),
                     DebugVariableCategory.Argument,
                     request.MaxChildren,
                     request.MaxStringLength,
                     request.IncludePrivate,
                     cancellationToken))
        {
            if (argumentNames.Add(argument.Name))
            {
                variables.Add(argument);
            }
        }
        var locals = CreateVariables(
            ReadDynamic(() => frame.Locals, null),
            DebugVariableCategory.Local,
            request.MaxChildren,
            request.MaxStringLength,
            request.IncludePrivate,
            cancellationToken);
        foreach (var local in locals)
        {
            if (argumentNames.Contains(local.Name))
            {
                diagnostics.Add($"Debugger locals included argument '{local.Name}'; duplicate local entry was omitted.");
                continue;
            }

            variables.Add(local);
        }

        if (!request.IncludePrivate)
        {
            diagnostics.Add("Data member expansion is disabled unless includePrivate=true because EnvDTE does not expose reliable member accessibility metadata.");
        }

        return Success(variables.ToArray(), diagnostics);
    }

    public async Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateExpressionAsync(
        DebugExpressionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Expression))
        {
            return Failure<DebugExpressionResult>("Expression is required.");
        }

        var validation = ValidateValueLimits(request.MaxChildren, request.MaxStringLength);
        if (validation is not null)
        {
            return Failure<DebugExpressionResult>(validation);
        }

        if (!request.AllowSideEffects)
        {
            return Failure<DebugExpressionResult>(
                "SideEffectsNotAllowed: EnvDTE expression evaluation cannot guarantee side-effect-free execution. Retry with allowSideEffects=true after reviewing the expression.");
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugExpressionResult>();
        }

        dynamic debugger = debuggerResult.Debugger!;
        if (ReadDebuggerState(debugger) != DebuggerState.Break)
        {
            return Failure<DebugExpressionResult>("DebuggerNotPaused: expression evaluation is available only when Visual Studio is in break mode.");
        }

        if (!string.IsNullOrWhiteSpace(request.FrameId))
        {
            var currentFrame = CreateStackFrameInfo(ReadDynamic(() => debugger.CurrentStackFrame, null), ReadDynamic(() => (int)debugger.CurrentThread.ID, 0), 0, true);
            if (!string.Equals(currentFrame?.FrameId, request.FrameId, StringComparison.Ordinal))
            {
                return Failure<DebugExpressionResult>(
                    "NonCurrentFrameEvaluationNotSupported: expression evaluation is restricted to the current debugger frame to avoid changing Visual Studio debugger selection.");
            }
        }

        try
        {
            dynamic expression = debugger.GetExpression(request.Expression, true, 5000);
            var result = new DebugExpressionResult
            {
                Expression = request.Expression,
                Succeeded = ReadDynamic(() => (bool)expression.IsValidValue, false),
                TypeName = ReadDynamic(() => (string)expression.Type, string.Empty),
                Value = SanitizeValue(request.Expression, ReadDynamic(() => (string)expression.Value, string.Empty), request.MaxStringLength).Value,
                Error = ReadDynamic(() => (string)expression.Value, string.Empty),
                SideEffectRisk = true,
                Children = CreateChildVariables(
                    ReadDynamic(() => expression.DataMembers, null),
                    request.MaxChildren,
                    request.MaxStringLength,
                    cancellationToken),
            };

            if (result.Succeeded)
            {
                result.Error = string.Empty;
            }

            return Success(new[] { result }, new[] { "Expression evaluation was executed with allowSideEffects=true; property getters or debugger visualizers may have run." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Success(
                new[]
                {
                    new DebugExpressionResult
                    {
                        Expression = request.Expression,
                        Succeeded = false,
                        Error = ex.Message,
                        SideEffectRisk = true,
                    },
                },
                new[] { "Expression evaluation failed in the Visual Studio debugger." },
                isPartial: true);
        }
    }

    public async Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListBreakpointsAsync(
        DebugBreakpointsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MaxResults is < 1 or > 5000)
        {
            return Failure<DebugBreakpointInfo>("MaxResults must be between 1 and 5000.");
        }

        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return debuggerResult.Failure.As<DebugBreakpointInfo>();
        }

        dynamic debugger = debuggerResult.Debugger!;
        var items = new List<DebugBreakpointInfo>();
        foreach (var breakpoint in EnumerateDynamic(ReadDynamic(() => debugger.Breakpoints, null)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CreateBreakpointInfo(breakpoint));
            if (items.Count >= request.MaxResults)
            {
                return Success(items.ToArray(), new[] { $"Breakpoints were truncated at MaxResults={request.MaxResults}." }, isPartial: true);
            }
        }

        return Success(items.ToArray());
    }

    private async Task<DebuggerLookupResult> TryGetDebuggerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)).ConfigureAwait(true);
            if (dte is null)
            {
                return DebuggerLookupResult.FromFailure("DebuggerUnavailable: EnvDTE service is not available.");
            }

            dynamic debugger = ((dynamic)dte).Debugger;
            return debugger is null
                ? DebuggerLookupResult.FromFailure("DebuggerUnavailable: EnvDTE.Debugger is not available.")
                : DebuggerLookupResult.Success(debugger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DebuggerLookupResult.FromFailure("DebuggerUnavailable: failed to obtain Visual Studio debugger: " + ex.Message);
        }
    }

    private async Task<FrameLookupResult> ResolveFrameAsync(
        string? frameId,
        CancellationToken cancellationToken)
    {
        var debuggerResult = await TryGetDebuggerAsync(cancellationToken).ConfigureAwait(false);
        if (debuggerResult.Failure is not null)
        {
            return FrameLookupResult.FromFailure(debuggerResult.Failure);
        }

        dynamic debugger = debuggerResult.Debugger!;
        if (ReadDebuggerState(debugger) != DebuggerState.Break)
        {
            return FrameLookupResult.FromFailure("DebuggerNotPaused: stack frame variables are available only when Visual Studio is in break mode.");
        }

        if (string.IsNullOrWhiteSpace(frameId))
        {
            var current = ReadDynamic(() => debugger.CurrentStackFrame, null);
            return current is null
                ? FrameLookupResult.FromFailure("FrameUnavailable: Visual Studio did not expose a current stack frame.")
                : FrameLookupResult.Success(current);
        }

        var requestedFrameId = frameId!;
        if (TryParseFrameId(requestedFrameId, out var requestedThreadId, out var requestedFrameIndex))
        {
            var currentThread = ReadDynamic<object?>(() => debugger.CurrentThread, null);
            var currentThreadId = currentThread is null
                ? 0
                : ReadDynamic(() => (int)((dynamic)currentThread).ID, 0);
            if (currentThread is not null && currentThreadId == requestedThreadId)
            {
                var frame = GetStackFrameByIndex(currentThread, requestedFrameIndex);
                if (frame is not null)
                {
                    return FrameLookupResult.Success(frame);
                }
            }
        }

        foreach (var thread in EnumerateThreads(debugger))
        {
            var threadId = ReadDynamic(() => (int)thread.ID, 0);
            var index = 0;
            foreach (var frame in EnumerateStackFrames(thread))
            {
                var info = CreateStackFrameInfo(frame, threadId, index, isCurrent: false);
                if (string.Equals(info?.FrameId, frameId, StringComparison.Ordinal))
                {
                    return FrameLookupResult.Success(frame);
                }

                index++;
            }
        }

        return FrameLookupResult.FromFailure("FrameNotFound: the requested debugger frame id is no longer valid.");
    }

    private static bool TryParseFrameId(
        string frameId,
        out int threadId,
        out int frameIndex)
    {
        threadId = 0;
        frameIndex = 0;
        var parts = frameId.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], out threadId)
            && int.TryParse(parts[1], out frameIndex)
            && threadId > 0
            && frameIndex >= 0;
    }

    private static DebuggerState ReadDebuggerState(dynamic debugger)
    {
        var mode = ReadDynamic(() => (EnvDTE.dbgDebugMode)debugger.CurrentMode, EnvDTE.dbgDebugMode.dbgDesignMode);
        return mode switch
        {
            EnvDTE.dbgDebugMode.dbgBreakMode => DebuggerState.Break,
            EnvDTE.dbgDebugMode.dbgRunMode => DebuggerState.Run,
            EnvDTE.dbgDebugMode.dbgDesignMode => DebuggerState.Design,
            _ => DebuggerState.Unknown,
        };
    }

    private static DebugProcessInfo? CreateProcessInfo(dynamic process)
    {
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

    private static DebugThreadInfo? CreateThreadInfo(dynamic thread, bool isCurrent)
    {
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
        dynamic frame,
        int threadId,
        int index,
        bool isCurrent)
    {
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

    private static DebugStackFrameInfo? CreateCurrentStackFrameInfo(dynamic debugger, int threadId)
    {
        var currentFrame = ReadDynamic(() => debugger.CurrentStackFrame, null);
        var currentThread = ReadDynamic(() => debugger.CurrentThread, null);
        if (currentFrame is null)
        {
            return null;
        }

        if (currentThread is not null)
        {
            var index = 0;
            foreach (var frame in EnumerateStackFrames(currentThread))
            {
                if (IsSameStackFrame(frame, currentFrame))
                {
                    return CreateStackFrameInfo(frame, threadId, index, isCurrent: true);
                }

                index++;
            }
        }

        return CreateStackFrameInfo(currentFrame, threadId, index: 0, isCurrent: true);
    }

    private static bool IsSameStackFrame(dynamic candidate, dynamic current)
    {
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

    private static IReadOnlyList<dynamic> EnumerateThreads(dynamic debugger)
    {
        var process = ReadDynamic(() => debugger.CurrentProcess, null);
        if (process is null)
        {
            return Array.Empty<dynamic>();
        }

        dynamic currentProcess = process;
        var threads = ReadDynamic(() => currentProcess.Threads, null);
        return EnumerateDynamic(threads);
    }

    private static dynamic? FindThread(dynamic debugger, int? threadId)
    {
        if (threadId is null)
        {
            return ReadDynamic(() => debugger.CurrentThread, null);
        }

        var currentThread = ReadDynamic(() => debugger.CurrentThread, null);
        if (currentThread is not null
            && ReadDynamic(() => (int)((dynamic)currentThread).ID, 0) == threadId.Value)
        {
            return currentThread;
        }

        foreach (dynamic thread in EnumerateThreads(debugger))
        {
            if (ReadDynamic(() => (int)thread.ID, 0) == threadId.Value)
            {
                return thread;
            }
        }

        return null;
    }

    private static IReadOnlyList<dynamic> EnumerateStackFrames(dynamic thread)
    {
        var frames = ReadDynamic(() => thread.StackFrames, null);
        return EnumerateDynamic(frames);
    }

    private static dynamic? GetStackFrameByIndex(dynamic thread, int frameIndex)
    {
        if (thread is null)
        {
            return null;
        }

        var index = 0;
        foreach (var frame in EnumerateStackFrames(thread))
        {
            if (index == frameIndex)
            {
                return frame;
            }

            index++;
        }

        return null;
    }

    private static IReadOnlyList<dynamic> EnumerateDynamic(dynamic collection)
    {
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

    private static IEnumerable<DebugVariableInfo> CreateVariables(
        dynamic expressions,
        DebugVariableCategory category,
        int maxChildren,
        int maxStringLength,
        bool includeDataMembers,
        CancellationToken cancellationToken)
    {
        foreach (var expression in EnumerateDynamic(expressions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateVariableInfo(expression, category, maxChildren, maxStringLength, includeDataMembers, cancellationToken);
        }
    }

    private static DebugVariableInfo CreateVariableInfo(
        dynamic expression,
        DebugVariableCategory category,
        int maxChildren,
        int maxStringLength,
        bool includeDataMembers,
        CancellationToken cancellationToken)
    {
        var name = ReadDynamic(() => (string)expression.Name, string.Empty);
        var value = SanitizeValue(name, ReadDynamic(() => (string)expression.Value, string.Empty), maxStringLength);
        var dataMembers = ReadDynamic(() => expression.DataMembers, null);
        var hasChildren = EnumerateDynamic(dataMembers).Count > 0;
        return new DebugVariableInfo
        {
            Name = name,
            TypeName = ReadDynamic(() => (string)expression.Type, string.Empty),
            Value = value.Value,
            Category = category,
            IsExpandable = hasChildren,
            IsSensitiveRedacted = value.IsRedacted,
            EvaluationError = ReadDynamic(() => (bool)expression.IsValidValue, true) ? string.Empty : ReadDynamic(() => (string)expression.Value, string.Empty),
            Children = includeDataMembers
                ? CreateChildVariables(dataMembers, maxChildren, maxStringLength, cancellationToken)
                : Array.Empty<DebugVariableInfo>(),
        };
    }

    private static IReadOnlyList<DebugVariableInfo> CreateChildVariables(
        dynamic expressions,
        int maxChildren,
        int maxStringLength,
        CancellationToken cancellationToken)
    {
        if (maxChildren == 0)
        {
            return Array.Empty<DebugVariableInfo>();
        }

        var children = new List<DebugVariableInfo>();
        foreach (dynamic expression in EnumerateDynamic(expressions))
        {
            children.Add(CreateVariableInfo(expression, DebugVariableCategory.DataMember, 0, maxStringLength, false, cancellationToken));
            if (children.Count >= maxChildren)
            {
                break;
            }
        }

        return children.ToArray();
    }

    private static DebugBreakpointInfo CreateBreakpointInfo(dynamic breakpoint)
    {
        var file = ReadDynamic(() => (string)breakpoint.File, string.Empty);
        var line = ReadDynamic(() => (int)breakpoint.FileLine, 0);
        var condition = ReadDynamic(() => (string)breakpoint.Condition, string.Empty);
        var hitCountMode = ReadDynamic(
            () => FromEnvDteHitCountType((EnvDTE.dbgHitCountType)breakpoint.HitCountType),
            DebugBreakpointHitCountMode.None);
        return new DebugBreakpointInfo
        {
            Name = ReadDynamic(() => (string)breakpoint.Name, string.Empty),
            FunctionName = ReadDynamic(() => (string)breakpoint.FunctionName, string.Empty),
            IsEnabled = ReadDynamic(() => (bool)breakpoint.Enabled, false),
            Condition = condition,
            ConditionMode = ReadDynamic(
                () => FromEnvDteConditionType((EnvDTE.dbgBreakpointConditionType)breakpoint.ConditionType, condition),
                DebugBreakpointConditionMode.None),
            HitCountTarget = hitCountMode == DebugBreakpointHitCountMode.None
                ? 0
                : ReadDynamic(() => (int)breakpoint.HitCountTarget, 0),
            HitCountMode = hitCountMode,
            CurrentHitCount = ReadDynamic(() => (int)breakpoint.CurrentHits, 0),
            Span = !string.IsNullOrWhiteSpace(file) && line > 0
                ? new SourceSpan
                {
                    FilePath = file,
                    StartLine = line,
                    StartColumn = 1,
                    EndLine = line,
                    EndColumn = 1,
                }
                : null,
        };
    }

    private static DebugBreakpointConditionMode FromEnvDteConditionType(EnvDTE.dbgBreakpointConditionType mode, string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return DebugBreakpointConditionMode.None;
        }

        return mode == EnvDTE.dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenChanged
            ? DebugBreakpointConditionMode.WhenChanged
            : DebugBreakpointConditionMode.WhenTrue;
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

    private static (string Value, bool IsRedacted) SanitizeValue(string name, string value, int maxStringLength)
    {
        if (IsSensitiveName(name))
        {
            return ("[redacted]", true);
        }

        if (value.Length > maxStringLength)
        {
            return (value.Substring(0, maxStringLength), false);
        }

        return (value, false);
    }

    private static bool IsSensitiveName(string name)
    {
        var sensitive = new[] { "password", "passwd", "pwd", "secret", "token", "authorization", "apikey", "api_key" };
        return sensitive.Any(item => name.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string? ValidateValueLimits(int maxChildren, int maxStringLength)
    {
        if (maxChildren is < 0 or > 500)
        {
            return "MaxChildren must be between 0 and 500.";
        }

        if (maxStringLength is < 1 or > 10000)
        {
            return "MaxStringLength must be between 1 and 10000.";
        }

        return null;
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

    private static WorkspaceQueryResult<T> Success<T>(
        IReadOnlyList<T> items,
        IReadOnlyList<string>? diagnostics = null,
        bool isPartial = false)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = items,
            Diagnostics = diagnostics ?? Array.Empty<string>(),
            IsPartial = isPartial,
        };
    }

    private static WorkspaceQueryResult<T> Failure<T>(string diagnostic)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = Array.Empty<T>(),
            Diagnostics = new[] { diagnostic },
            IsPartial = true,
        };
    }

    private sealed class DebuggerLookupResult
    {
        private DebuggerLookupResult(object? debugger, QueryFailure? failure)
        {
            Debugger = debugger;
            Failure = failure;
        }

        public object? Debugger { get; }

        public QueryFailure? Failure { get; }

        public static DebuggerLookupResult Success(object debugger) => new(debugger, null);

        public static DebuggerLookupResult FromFailure(string diagnostic) => new(null, new QueryFailure(diagnostic));
    }

    private sealed class FrameLookupResult
    {
        private FrameLookupResult(object? frame, QueryFailure? failure)
        {
            Frame = frame;
            Failure = failure;
        }

        public object? Frame { get; }

        public QueryFailure? Failure { get; }

        public static FrameLookupResult Success(object frame) => new(frame, null);

        public static FrameLookupResult FromFailure(QueryFailure failure) => new(null, failure);

        public static FrameLookupResult FromFailure(string diagnostic) => new(null, new QueryFailure(diagnostic));
    }

    private sealed class QueryFailure
    {
        public QueryFailure(string diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public string Diagnostic { get; }

        public WorkspaceQueryResult<T> As<T>() => Failure<T>(Diagnostic);
    }
}
