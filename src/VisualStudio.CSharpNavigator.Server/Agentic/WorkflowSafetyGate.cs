using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class WorkflowSafetyGate
{
    public SafetyBlocker[] EvaluateEditTask(CSharpTaskContextPackage taskContext)
    {
        var blockers = new List<SafetyBlocker>();

        if (string.Equals(taskContext.Status, "NoActiveBridge", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "NoActiveBridge",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "当前没有可用的 Visual Studio bridge，不能提供 Roslyn 语义级编辑候选。",
                SuggestedAction = "先用 prepare_csharp_workspace 或 open_csharp_solution_in_visual_studio 打开目标 solution。",
            });
        }

        if (string.Equals(taskContext.Status, "AmbiguousTarget", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "AmbiguousTarget",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "当前存在多个 Visual Studio 目标，不能猜测编辑上下文。",
                SuggestedAction = "传入 targetInstanceId、targetPipeName 或 targetSolutionPath 消歧。",
            });
        }

        if (string.Equals(taskContext.Status, "SolutionNotLoaded", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "SolutionNotLoaded",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "目标 Visual Studio 实例没有加载可用 C# solution。",
                SuggestedAction = "在 Visual Studio 中加载目标 .sln 或 .slnx 后重试。",
            });
        }

        return blockers.ToArray();
    }

    public SafetyBlocker[] EvaluateChangeReview(CSharpChangeReviewReport review)
    {
        var blockers = new List<SafetyBlocker>();

        AddCommonWorkspaceBlockers(review.Status, blockers);

        if (review.TaskContext.ChangedFiles.Length == 0
            && string.IsNullOrWhiteSpace(review.TaskContext.SymbolQuery)
            && review.PrimaryFiles.Length == 0)
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "UnscopedReview",
                Severity = AgentWorkflowSafetySeverity.Warning,
                Message = "当前审查缺少 changedFiles、symbolQuery 或明确文件范围，结果只能作为背景证据。",
                SuggestedAction = "传入 changedFiles、areaPaths、symbolQuery 或 projectName 重新收窄审查范围。",
            });
        }

        return blockers.ToArray();
    }

    public SafetyBlocker[] EvaluateVerificationRun(CSharpRegressionScopePlan plan)
    {
        var blockers = new List<SafetyBlocker>();

        AddCommonWorkspaceBlockers(plan.VerificationPlan.Status, blockers);

        if (plan.SmokeCommands.Length == 0 && plan.FocusedCommands.Length == 0 && plan.BroadCommands.Length == 0)
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "NoVerificationCommands",
                Severity = AgentWorkflowSafetySeverity.Warning,
                Message = "当前没有形成可执行的 build/test 验证命令。",
                SuggestedAction = "传入 changedFiles、filePath、symbolQuery、projectName 或 build output 重新生成验证计划。",
            });
        }

        return blockers.ToArray();
    }

    public SafetyBlocker[] EvaluateRuntimeException(DebugSessionPreparationPlan plan)
    {
        var blockers = new List<SafetyBlocker>();

        AddCommonWorkspaceBlockers(plan.Status, blockers);

        if (plan.DebuggerStatus is null || !plan.DebuggerStatus.IsDebugging)
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "NoActiveDebugSession",
                Severity = AgentWorkflowSafetySeverity.Warning,
                Message = "当前没有活动调试会话，runtime exception 调查包只能提供启动和证据采集建议。",
                SuggestedAction = "先明确目标后使用 start_debugging 或业务 runner 触发异常，再重新收集调试证据。",
            });
        }
        else if (!plan.DebuggerStatus.IsPaused)
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "DebuggerNotPaused",
                Severity = AgentWorkflowSafetySeverity.Warning,
                Message = "调试器正在运行但未暂停，暂时无法读取当前调用栈和局部变量上下文。",
                SuggestedAction = "等待断点或异常命中，或使用 break_debugging 后重新收集调试证据。",
            });
        }

        return blockers.ToArray();
    }

    private static void AddCommonWorkspaceBlockers(string status, List<SafetyBlocker> blockers)
    {
        if (string.Equals(status, "NoActiveBridge", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "NoActiveBridge",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "当前没有可用的 Visual Studio bridge，不能提供 Roslyn 语义级审查证据。",
                SuggestedAction = "先用 prepare_csharp_workspace 或 open_csharp_solution_in_visual_studio 打开目标 solution。",
            });
        }

        if (string.Equals(status, "AmbiguousTarget", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "AmbiguousTarget",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "当前存在多个 Visual Studio 目标，不能猜测审查上下文。",
                SuggestedAction = "传入 targetInstanceId、targetPipeName 或 targetSolutionPath 消歧。",
            });
        }

        if (string.Equals(status, "SolutionNotLoaded", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(new SafetyBlocker
            {
                Code = "SolutionNotLoaded",
                Severity = AgentWorkflowSafetySeverity.Blocker,
                Message = "目标 Visual Studio 实例没有加载可用 C# solution。",
                SuggestedAction = "在 Visual Studio 中加载目标 .sln 或 .slnx 后重试。",
            });
        }
    }
}
