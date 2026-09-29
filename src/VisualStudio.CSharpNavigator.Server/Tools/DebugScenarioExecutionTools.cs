using System.ComponentModel;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

[McpServerToolType]
public sealed class DebugScenarioExecutionTools
{
    private readonly CodeNavigationTools _inner;
    private readonly DebugScenarioExecutionStore _store;

    public DebugScenarioExecutionTools(CodeNavigationTools inner, DebugScenarioExecutionStore? store = null)
    {
        _inner = inner;
        _store = store ?? new DebugScenarioExecutionStore();
    }

    [McpServerTool(Name = "execute_csharp_debug_scenario", ReadOnly = false, Idempotent = false)]
    [Description("Execute a bounded C# debug scenario with explicit Visual Studio target, owned breakpoint/session cleanup, stop snapshots, and artifact evidence.")]
    public async Task<WorkspaceQueryResult<CSharpDebugScenarioExecution>> ExecuteCSharpDebugScenario(
        string? scenarioName = null,
        string? problemText = null,
        string? breakpointFilePath = null,
        int? breakpointLine = null,
        string[]? artifactPaths = null,
        bool stopDebuggingAtEnd = true,
        int waitTimeoutMilliseconds = 30000,
        string? targetPipeName = null,
        string? targetInstanceId = null,
        string? targetSolutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var target = new VisualStudioBridgeTarget
        {
            PipeName = targetPipeName ?? string.Empty,
            InstanceId = targetInstanceId ?? string.Empty,
            SolutionPath = targetSolutionPath ?? string.Empty,
        };
        if (target.IsEmpty)
        {
            return Failure<CSharpDebugScenarioExecution>(
                "DebugScenarioTargetRequired: execution requires targetPipeName, targetInstanceId, or targetSolutionPath.");
        }

        if (breakpointLine is < 1)
        {
            return Failure<CSharpDebugScenarioExecution>("BreakpointLine must be positive when provided.");
        }

        if (waitTimeoutMilliseconds is < 0 or > 300000)
        {
            return Failure<CSharpDebugScenarioExecution>("WaitTimeoutMilliseconds must be between 0 and 300000.");
        }

        var execution = new CSharpDebugScenarioExecution
        {
            ExecutionId = $"debug-{Guid.NewGuid():N}",
            ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "debug-scenario" : scenarioName!,
            State = DebugScenarioExecutionState.Running,
            Target = target,
            CleanupRequested = stopDebuggingAtEnd,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        _store.Add(execution);

        try
        {
            if (!string.IsNullOrWhiteSpace(breakpointFilePath))
            {
                var breakpoint = await _inner.SetDebugBreakpoint(
                        breakpointFilePath!,
                        breakpointLine ?? 1,
                        targetPipeName: target.PipeName,
                        targetInstanceId: target.InstanceId,
                        targetSolutionPath: target.SolutionPath,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                AddStep(execution, "configure", "set_debug_breakpoint", breakpoint);
                execution.Diagnostics = execution.Diagnostics.Concat(breakpoint.Diagnostics).ToArray();
                var breakpointResult = breakpoint.Items.FirstOrDefault();
                execution.CreatedBreakpoint = breakpointResult?.Succeeded == true;
                execution.OwnedBreakpointName = breakpointResult?.Breakpoint?.Name ?? string.Empty;
                if (breakpoint.IsPartial || breakpointResult?.Succeeded != true)
                {
                    await CleanupOwnedResourcesAsync(execution, cancellationToken).ConfigureAwait(false);
                    return Complete(execution, DebugScenarioExecutionState.Failed);
                }
            }

            var start = await _inner.StartDebugging(
                    targetPipeName: target.PipeName,
                    targetInstanceId: target.InstanceId,
                    targetSolutionPath: target.SolutionPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AddStep(execution, "start", "start_debugging", start);
            execution.Diagnostics = execution.Diagnostics.Concat(start.Diagnostics).ToArray();
            execution.StartedDebugger = start.Items.FirstOrDefault()?.Succeeded == true;
            if (start.IsPartial || !execution.StartedDebugger)
            {
                await CleanupOwnedResourcesAsync(execution, cancellationToken).ConfigureAwait(false);
                return Complete(execution, DebugScenarioExecutionState.Failed);
            }

            var wait = await WaitForDebugEvidenceAsync(execution, waitTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
            execution.Diagnostics = execution.Diagnostics.Concat(wait.Diagnostics).ToArray();
            if (wait.IsPartial)
            {
                execution.State = DebugScenarioExecutionState.OutcomeUnknown;
            }

            execution.BeforeStopSnapshot = wait.Items.FirstOrDefault();
            if (artifactPaths is { Length: > 0 })
            {
                var artifacts = await _inner.CollectArtifactEvidence(
                        artifactPaths,
                        maxArtifacts: 20,
                        maxLinesPerArtifact: 20,
                        maxCharsPerArtifact: 12000,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                execution.ArtifactEvidence = artifacts.Items.FirstOrDefault();
                execution.Diagnostics = execution.Diagnostics.Concat(artifacts.Diagnostics).ToArray();
                AddStep(execution, "collect", "collect_artifact_evidence", artifacts);
            }

            if (stopDebuggingAtEnd && execution.StartedDebugger)
            {
                await CleanupOwnedResourcesAsync(execution, cancellationToken).ConfigureAwait(false);
            }

            return Complete(
                execution,
                execution.CleanupRequested && !execution.CleanupCompleted
                    ? DebugScenarioExecutionState.CleanupIncomplete
                    : DebugScenarioExecutionState.Completed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await CleanupOwnedResourcesAsync(execution, CancellationToken.None).ConfigureAwait(false);
            execution.Diagnostics = execution.Diagnostics
                .Append("DebugScenarioExecutionDeadlineExceeded: execution stopped at its wait or bridge boundary; debugger state may be unknown.")
                .ToArray();
            return Complete(execution, DebugScenarioExecutionState.OutcomeUnknown);
        }
    }

    private async Task CleanupOwnedResourcesAsync(
        CSharpDebugScenarioExecution execution,
        CancellationToken cancellationToken)
    {
        var cleanupSucceeded = true;
        if (execution.StartedDebugger && execution.CleanupRequested)
        {
            var stop = await _inner.StopDebugging(
                    targetPipeName: execution.Target.PipeName,
                    targetInstanceId: execution.Target.InstanceId,
                    targetSolutionPath: execution.Target.SolutionPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AddStep(execution, "cleanup", "stop_debugging", stop);
            execution.Diagnostics = execution.Diagnostics.Concat(stop.Diagnostics).ToArray();
            cleanupSucceeded &= !stop.IsPartial && stop.Items.FirstOrDefault()?.Succeeded == true;

            var afterStop = await _inner.PrepareDebugSession(
                    targetPipeName: execution.Target.PipeName,
                    targetInstanceId: execution.Target.InstanceId,
                    targetSolutionPath: execution.Target.SolutionPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            execution.AfterStopSnapshot = afterStop.Items.FirstOrDefault();
            execution.Diagnostics = execution.Diagnostics.Concat(afterStop.Diagnostics).ToArray();
            cleanupSucceeded &= !afterStop.IsPartial
                && execution.AfterStopSnapshot?.DebuggerStatus?.IsDebugging != true;
        }

        if (execution.CreatedBreakpoint && !string.IsNullOrWhiteSpace(execution.OwnedBreakpointName))
        {
            var removed = await _inner.RemoveDebugBreakpoint(
                    breakpointName: execution.OwnedBreakpointName,
                    targetPipeName: execution.Target.PipeName,
                    targetInstanceId: execution.Target.InstanceId,
                    targetSolutionPath: execution.Target.SolutionPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AddStep(execution, "cleanup", "remove_debug_breakpoint", removed);
            execution.Diagnostics = execution.Diagnostics.Concat(removed.Diagnostics).ToArray();
            cleanupSucceeded &= !removed.IsPartial && removed.Items.FirstOrDefault()?.Succeeded == true;
        }

        execution.CleanupCompleted = cleanupSucceeded;
    }

    [McpServerTool(Name = "get_csharp_debug_scenario_execution", ReadOnly = true, Idempotent = true)]
    [Description("Read the bounded local result of a C# debug scenario execution by execution id; missing records are explicitly reported as unknown.")]
    public Task<WorkspaceQueryResult<CSharpDebugScenarioExecution>> GetCSharpDebugScenarioExecution(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_store.TryGet(executionId?.Trim() ?? string.Empty, out var execution))
        {
            return Task.FromResult(new WorkspaceQueryResult<CSharpDebugScenarioExecution>
            {
                IsPartial = true,
                Diagnostics = new[] { "DebugScenarioExecutionNotFoundOrExpired: execution ownership/result is no longer available." },
            });
        }

        return Task.FromResult(new WorkspaceQueryResult<CSharpDebugScenarioExecution>
        {
            Items = new[] { execution },
            IsPartial = execution.State is DebugScenarioExecutionState.CleanupIncomplete or DebugScenarioExecutionState.OutcomeUnknown,
            Diagnostics = execution.Diagnostics,
        });
    }

    private async Task<WorkspaceQueryResult<DebugSessionPreparationPlan>> WaitForDebugEvidenceAsync(
        CSharpDebugScenarioExecution execution,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        WorkspaceQueryResult<DebugSessionPreparationPlan>? latest = null;
        do
        {
            latest = await _inner.PrepareDebugSession(
                    targetPipeName: execution.Target.PipeName,
                    targetInstanceId: execution.Target.InstanceId,
                    targetSolutionPath: execution.Target.SolutionPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AddStep(execution, "wait", "prepare_debug_session", latest);
            var status = latest.Items.FirstOrDefault()?.DebuggerStatus;
            // Wait for break mode, not just "debugging started": the beforeStopSnapshot
            // collects call stacks and variables, which only exist while paused at the
            // configured breakpoint. Returning while still running defeats the purpose.
            if (status?.IsPaused == true || timeoutMilliseconds == 0 || status is { IsDebugging: false })
            {
                return latest;
            }

            if ((DateTimeOffset.UtcNow - started).TotalMilliseconds >= timeoutMilliseconds)
            {
                return new WorkspaceQueryResult<DebugSessionPreparationPlan>
                {
                    Items = latest.Items,
                    Diagnostics = latest.Diagnostics.Concat(new[]
                    {
                        $"DebugScenarioWaitTimedOut: timeoutMilliseconds={timeoutMilliseconds}.",
                    }).ToArray(),
                    IsPartial = true,
                };
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        while (true);
    }

    private static void AddStep<T>(
        CSharpDebugScenarioExecution execution,
        string phase,
        string toolName,
        WorkspaceQueryResult<T> result)
    {
        execution.Steps = execution.Steps
            .Append(new CSharpDebugScenarioStepResult
            {
                Order = execution.Steps.Length + 1,
                Phase = phase,
                ToolName = toolName,
                Succeeded = !result.IsPartial && result.Items.Count > 0,
                IsPartial = result.IsPartial,
                Diagnostics = result.Diagnostics.ToArray(),
            })
            .ToArray();
    }

    private WorkspaceQueryResult<CSharpDebugScenarioExecution> Complete(
        CSharpDebugScenarioExecution execution,
        DebugScenarioExecutionState state)
    {
        execution.State = state;
        execution.CompletedUtc = DateTimeOffset.UtcNow;
        _store.Add(execution);
        return new WorkspaceQueryResult<CSharpDebugScenarioExecution>
        {
            Items = new[] { DebugScenarioExecutionStore.Clone(execution) },
            Diagnostics = execution.Diagnostics,
            IsPartial = state is DebugScenarioExecutionState.CleanupIncomplete or DebugScenarioExecutionState.OutcomeUnknown,
        };
    }

    private static WorkspaceQueryResult<CSharpDebugScenarioExecution> Failure<T>(
        string diagnostic)
    {
        return new WorkspaceQueryResult<CSharpDebugScenarioExecution>
        {
            IsPartial = true,
            Succeeded = false,
            Diagnostics = new[] { diagnostic },
        };
    }
}
