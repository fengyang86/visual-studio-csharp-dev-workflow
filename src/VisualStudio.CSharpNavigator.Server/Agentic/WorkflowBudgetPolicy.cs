using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class WorkflowBudgetPolicy
{
    public WorkflowBudget CreateDefaultEditBudget(CSharpEditTaskRequest request)
    {
        return new WorkflowBudget
        {
            MaxToolCalls = 4,
            MaxReturnedChars = Math.Max(4000, request.MaxSourceSnippets * Math.Max(1000, request.MaxCharsPerSnippet)),
            MaxElapsedMilliseconds = NormalizeDeadline(request.MaxElapsedMilliseconds),
            MaxProjects = 20,
            MaxDiagnostics = request.MaxDiagnostics,
            MaxReferences = request.MaxRelatedItems,
            AllowWholeSolution = request.IncludeWholeSolutionDiagnostics,
        };
    }

    public WorkflowBudget CreateDefaultChangeReviewBudget(CSharpChangeReviewTaskRequest request)
    {
        return new WorkflowBudget
        {
            MaxToolCalls = 5,
            MaxReturnedChars = 24000,
            MaxElapsedMilliseconds = NormalizeDeadline(request.MaxElapsedMilliseconds),
            MaxProjects = 20,
            MaxDiagnostics = request.MaxDiagnostics,
            MaxReferences = request.MaxRelatedItems,
            AllowWholeSolution = false,
        };
    }

    public WorkflowBudget CreateDefaultVerificationRunBudget(CSharpVerificationRunTaskRequest request)
    {
        return new WorkflowBudget
        {
            MaxToolCalls = 5,
            MaxReturnedChars = 20000,
            MaxElapsedMilliseconds = NormalizeDeadline(request.MaxElapsedMilliseconds),
            MaxProjects = 20,
            MaxDiagnostics = request.MaxDiagnostics,
            MaxReferences = request.MaxRelatedItems,
            AllowWholeSolution = false,
        };
    }

    public WorkflowBudget CreateDefaultRuntimeExceptionBudget(CSharpRuntimeExceptionTaskRequest request)
    {
        return new WorkflowBudget
        {
            MaxToolCalls = request.ArtifactPaths.Length > 0 ? 5 : 4,
            MaxReturnedChars = Math.Max(12000, request.MaxSourceSnippets * Math.Max(1000, request.MaxCharsPerSnippet)),
            MaxElapsedMilliseconds = NormalizeDeadline(request.MaxElapsedMilliseconds),
            MaxProjects = 10,
            MaxDiagnostics = 0,
            MaxReferences = request.MaxFrames,
            AllowWholeSolution = false,
        };
    }

    private static int NormalizeDeadline(int value)
    {
        return Math.Clamp(value, 1000, 55000);
    }
}
