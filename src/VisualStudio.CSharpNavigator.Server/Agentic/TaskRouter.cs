using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class TaskRouter
{
    public RecommendedNextAction[] RouteEditTask(CSharpEditTaskRequest request, CSharpTaskContextPackage context)
    {
        var actions = new List<RecommendedNextAction>();
        AddWorkspaceActions(actions, context.Status);

        if (HasBuildEvidence(request.BuildOutput, request.BuildLogFilePath) || context.PrimaryDiagnostics.Length > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectDiagnostic,
                EvidenceLevel = HasBuildEvidence(request.BuildOutput, request.BuildLogFilePath)
                    ? WorkflowEvidenceLevel.Fact
                    : WorkflowEvidenceLevel.Inference,
                Reason = "当前任务包含构建输出、构建日志或主诊断，应先用构建失败工作流锁定根因，再进入源码编辑。",
                Confidence = "high",
                TargetFilePath = context.PrimaryDiagnostics.FirstOrDefault()?.Span?.FilePath ?? request.FilePath ?? string.Empty,
                TargetSpan = context.PrimaryDiagnostics.FirstOrDefault()?.Span,
                TargetProjectName = context.PrimaryDiagnostics.FirstOrDefault()?.ProjectName ?? request.ProjectName ?? string.Empty,
                SuggestedTool = "investigate_csharp_build_failure",
            });
        }

        if (context.SourceSnippets.Length == 0
            && (!string.IsNullOrWhiteSpace(request.FilePath)
                || !string.IsNullOrWhiteSpace(request.SymbolQuery)
                || context.PrimarySymbols.Length > 0
                || context.PrimaryFiles.Length > 0))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = context.PrimarySymbols.Length > 0 ? WorkflowActionKind.InspectSymbol : WorkflowActionKind.InspectFile,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Reason = "已有文件或符号线索，但还没有源码切片；编辑前应先获取 bounded source snippet。",
                Confidence = "high",
                TargetFilePath = context.PrimaryFiles.FirstOrDefault()?.FilePath ?? request.FilePath ?? context.PrimarySymbols.FirstOrDefault()?.Symbol.Span?.FilePath ?? string.Empty,
                TargetSpan = context.PrimarySymbols.FirstOrDefault()?.Span ?? context.PrimaryFiles.FirstOrDefault()?.Span,
                TargetSymbol = context.PrimarySymbols.FirstOrDefault()?.Symbol,
                TargetProjectName = request.ProjectName ?? context.PrimarySymbols.FirstOrDefault()?.Symbol.ProjectName ?? string.Empty,
                SuggestedTool = context.PrimarySymbols.Length > 0 ? "get_csharp_symbol_source" : "get_csharp_source_context",
            });
        }

        if (HasAny(request.ChangedFiles))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunBuild,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Reason = "任务已提供 changedFiles，编辑后应生成最小验证计划，而不是直接跑全 solution。",
                Confidence = "medium",
                TargetFilePath = request.ChangedFiles.FirstOrDefault() ?? string.Empty,
                TargetProjectName = request.ProjectName ?? string.Empty,
                SuggestedTool = "prepare_csharp_verification_run",
            });
        }

        if (actions.Count == 0 && string.IsNullOrWhiteSpace(request.FilePath) && string.IsNullOrWhiteSpace(request.SymbolQuery))
        {
            actions.Add(CreateNarrowScopeAction("编辑任务缺少 filePath、symbolQuery、changedFiles 或 build output，当前只能给背景建议。"));
        }

        return Merge(context.RecommendedNextActions, actions);
    }

    public RecommendedNextAction[] RouteChangeReview(CSharpChangeReviewTaskRequest request, CSharpChangeReviewReport review)
    {
        var actions = new List<RecommendedNextAction>();
        AddWorkspaceActions(actions, review.Status);

        if (!HasAny(request.ChangedFiles) && !HasAny(request.AreaPaths) && string.IsNullOrWhiteSpace(request.SymbolQuery))
        {
            actions.Add(CreateNarrowScopeAction("变更审查缺少 changedFiles、areaPaths 或 symbolQuery，结果容易退化成背景噪声。"));
        }

        if (review.PublicApiRisks.Length > 0 || review.ImpactSummary is { HasCrossProjectImpact: true })
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectSymbol,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Reason = "审查发现公共 API 或跨项目影响风险，应优先检查符号影响和调用方。",
                Confidence = "high",
                TargetSymbol = review.ImpactSummary?.Symbol ?? review.PrimarySymbols.FirstOrDefault()?.Symbol,
                TargetProjectName = review.ImpactSummary?.Symbol.ProjectName ?? request.ProjectName ?? string.Empty,
                SuggestedTool = "analyze_csharp_symbol_impact",
            });
        }

        if (review.TestGaps.Length > 0 || HasAny(request.ChangedFiles))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Reason = "审查有测试缺口或 changedFiles，应生成 smoke/focused/broad 分层验证计划。",
                Confidence = "medium",
                TargetFilePath = request.ChangedFiles.FirstOrDefault() ?? string.Empty,
                TargetProjectName = request.ProjectName ?? string.Empty,
                SuggestedTool = "prepare_csharp_verification_run",
            });
        }

        return Merge(review.RecommendedNextActions, actions);
    }

    public RecommendedNextAction[] RouteVerificationRun(CSharpVerificationRunTaskRequest request, CSharpRegressionScopePlan plan)
    {
        var actions = new List<RecommendedNextAction>();
        AddWorkspaceActions(actions, plan.VerificationPlan.Status);

        var focusedTest = plan.FocusedCommands.FirstOrDefault(command =>
            command.Command.Contains(" test ", StringComparison.OrdinalIgnoreCase));
        if (focusedTest is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.RunTests,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Reason = "验证计划已生成 focused test 命令，应先跑最小相关测试。",
                Confidence = "high",
                TargetProjectName = focusedTest.Scope,
                SuggestedTool = "shell",
                SuggestedCommand = focusedTest.Command,
            });
        }

        var smokeBuild = plan.SmokeCommands.FirstOrDefault()
            ?? plan.FocusedCommands.FirstOrDefault()
            ?? plan.BroadCommands.FirstOrDefault();
        if (smokeBuild is not null)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = smokeBuild.Command.Contains(" test ", StringComparison.OrdinalIgnoreCase)
                    ? WorkflowActionKind.RunTests
                    : WorkflowActionKind.RunBuild,
                EvidenceLevel = WorkflowEvidenceLevel.Inference,
                Reason = "验证计划已生成可执行命令，应按 smoke、focused、broad 顺序递进。",
                Confidence = "high",
                TargetProjectName = smokeBuild.Scope,
                SuggestedTool = "shell",
                SuggestedCommand = smokeBuild.Command,
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(CreateNarrowScopeAction("验证任务没有形成命令，需要补充 changedFiles、filePath、symbolQuery、projectName 或 build output。"));
        }

        return Merge(plan.RecommendedNextActions, actions);
    }

    public RecommendedNextAction[] RouteRuntimeException(CSharpRuntimeExceptionTaskRequest request, DebugSessionPreparationPlan plan, ArtifactEvidenceReport? artifactEvidence)
    {
        var actions = new List<RecommendedNextAction>();
        AddWorkspaceActions(actions, plan.Status);

        if (artifactEvidence is not null && artifactEvidence.Artifacts.Length > 0)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.InspectArtifact,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Reason = "已收集运行产物，应优先读取 artifact resource 中的错误、警告和关键片段。",
                Confidence = "high",
                SuggestedTool = "collect_artifact_evidence",
            });
        }

        if (plan.DebuggerStatus is null || !plan.DebuggerStatus.IsDebugging)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.StartDebugging,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Reason = "当前没有活动调试会话，运行时异常调查需要先启动或附加调试目标。",
                Confidence = "medium",
                SuggestedTool = "start_debugging",
            });
        }
        else if (!plan.DebuggerStatus.IsPaused)
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.CollectDebugContext,
                EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
                Reason = "调试器未暂停，需等待断点/异常或手动 break 后再收集调用栈和局部变量。",
                Confidence = "medium",
                SuggestedTool = "prepare_debug_session",
            });
        }

        if (request.ArtifactPaths.Length == 0 && string.IsNullOrWhiteSpace(request.ExceptionText))
        {
            actions.Add(CreateNarrowScopeAction("运行时异常任务缺少 exceptionText 或 artifactPaths，建议补充异常文本、日志或报告路径。"));
        }

        return Merge(plan.RecommendedNextActions, actions);
    }

    private static void AddWorkspaceActions(List<RecommendedNextAction> actions, string status)
    {
        if (string.Equals(status, "NoActiveBridge", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.OpenVisualStudio,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Reason = "当前没有可用 Visual Studio bridge，必须先准备或打开 C# solution。",
                Confidence = "high",
                SuggestedTool = "prepare_csharp_workspace",
            });
        }
        else if (string.Equals(status, "AmbiguousTarget", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.SelectTarget,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Reason = "当前存在多个可用 Visual Studio 目标，后续语义工具需要显式 target。",
                Confidence = "high",
                SuggestedTool = "list_visual_studio_instances",
            });
        }
        else if (string.Equals(status, "SolutionNotLoaded", StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new RecommendedNextAction
            {
                Kind = WorkflowActionKind.OpenVisualStudio,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Reason = "目标 Visual Studio 实例没有加载 C# solution，需先打开 .sln 或 .slnx。",
                Confidence = "high",
                SuggestedTool = "open_csharp_solution_in_visual_studio",
            });
        }
    }

    private static RecommendedNextAction CreateNarrowScopeAction(string reason)
    {
        return new RecommendedNextAction
        {
            Kind = WorkflowActionKind.NarrowScope,
            EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
            Reason = reason,
            Confidence = "medium",
            SuggestedTool = "prepare_csharp_edit_task",
        };
    }

    private static RecommendedNextAction[] Merge(
        IEnumerable<RecommendedNextAction> existing,
        IEnumerable<RecommendedNextAction> routed)
    {
        return existing
            .Concat(routed)
            .Where(action => action.Kind != WorkflowActionKind.Unknown || !string.IsNullOrWhiteSpace(action.SuggestedTool))
            .GroupBy(action => CreateActionKey(action), StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(12)
            .ToArray();
    }

    private static string CreateActionKey(RecommendedNextAction action)
    {
        return string.Join(
            "|",
            action.Kind.ToString(),
            action.SuggestedTool ?? string.Empty,
            action.TargetFilePath ?? string.Empty,
            action.TargetProjectName ?? string.Empty,
            action.SuggestedCommand ?? string.Empty);
    }

    private static bool HasBuildEvidence(string? buildOutput, string? buildLogFilePath)
    {
        return !string.IsNullOrWhiteSpace(buildOutput) || !string.IsNullOrWhiteSpace(buildLogFilePath);
    }

    private static bool HasAny<T>(IReadOnlyCollection<T>? values)
    {
        return values is { Count: > 0 };
    }
}
