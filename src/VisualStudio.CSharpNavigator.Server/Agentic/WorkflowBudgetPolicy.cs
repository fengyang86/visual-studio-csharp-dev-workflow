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
            MaxElapsedMilliseconds = 30000,
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
            MaxElapsedMilliseconds = 30000,
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
            MaxElapsedMilliseconds = 30000,
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
            MaxElapsedMilliseconds = 30000,
            MaxProjects = 10,
            MaxDiagnostics = 0,
            MaxReferences = request.MaxFrames,
            AllowWholeSolution = false,
        };
    }
}
