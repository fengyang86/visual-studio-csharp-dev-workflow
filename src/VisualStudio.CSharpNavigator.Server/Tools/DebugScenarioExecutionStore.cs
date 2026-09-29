using System.Collections.Concurrent;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public sealed class DebugScenarioExecutionStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);
    private const int MaxExecutionCount = 128;
    private readonly ConcurrentDictionary<string, CSharpDebugScenarioExecution> _executions = new(StringComparer.Ordinal);

    public void Add(CSharpDebugScenarioExecution execution)
    {
        RemoveExpired();
        _executions[execution.ExecutionId] = Clone(execution);
        while (_executions.Count > MaxExecutionCount)
        {
            var oldest = _executions.Values
                .OrderBy(item => item.CompletedUtc ?? item.StartedUtc)
                .FirstOrDefault();
            if (oldest is null)
            {
                break;
            }

            _executions.TryRemove(oldest.ExecutionId, out _);
        }
    }

    public bool TryGet(string executionId, out CSharpDebugScenarioExecution execution)
    {
        RemoveExpired();
        if (_executions.TryGetValue(executionId, out var stored))
        {
            execution = Clone(stored);
            return true;
        }

        execution = new CSharpDebugScenarioExecution();
        return false;
    }

    private void RemoveExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var pair in _executions)
        {
            if (pair.Value.CompletedUtc is { } completed && completed < cutoff)
            {
                _executions.TryRemove(pair.Key, out _);
            }
        }
    }

    public static CSharpDebugScenarioExecution Clone(CSharpDebugScenarioExecution source)
    {
        return new CSharpDebugScenarioExecution
        {
            ExecutionId = source.ExecutionId,
            ScenarioName = source.ScenarioName,
            State = source.State,
            Target = new VisualStudioBridgeTarget
            {
                PipeName = source.Target.PipeName,
                InstanceId = source.Target.InstanceId,
                SolutionPath = source.Target.SolutionPath,
            },
            StartedDebugger = source.StartedDebugger,
            CreatedBreakpoint = source.CreatedBreakpoint,
            OwnedBreakpointName = source.OwnedBreakpointName,
            CleanupRequested = source.CleanupRequested,
            CleanupCompleted = source.CleanupCompleted,
            BeforeStopSnapshot = source.BeforeStopSnapshot,
            AfterStopSnapshot = source.AfterStopSnapshot,
            ArtifactEvidence = source.ArtifactEvidence,
            Steps = source.Steps.ToArray(),
            Diagnostics = source.Diagnostics.ToArray(),
            StartedUtc = source.StartedUtc,
            CompletedUtc = source.CompletedUtc,
        };
    }
}
