using System.Diagnostics;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class WorkflowTelemetryRecorder
{
    public WorkflowTelemetry CreateTelemetry(
        string workflowName,
        Stopwatch stopwatch,
        int returnedItemCount,
        int returnedCharacterCount,
        int resourceLinkCount,
        bool isPartial,
        IEnumerable<string> diagnostics)
    {
        return new WorkflowTelemetry
        {
            WorkflowName = workflowName,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            ReturnedItemCount = returnedItemCount,
            ReturnedCharacterCount = returnedCharacterCount,
            ResourceLinkCount = resourceLinkCount,
            PartialReasons = isPartial ? diagnostics.Take(8).ToArray() : Array.Empty<string>(),
        };
    }
}
