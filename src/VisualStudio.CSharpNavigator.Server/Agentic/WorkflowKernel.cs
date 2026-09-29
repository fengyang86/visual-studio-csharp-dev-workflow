using System.Diagnostics;
using System.Text.Json;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class WorkflowKernel
{
    private readonly CodeNavigationTools _tools;
    private readonly EvidencePacketBuilder _evidencePacketBuilder;
    private readonly WorkflowBudgetPolicy _budgetPolicy;
    private readonly WorkflowSafetyGate _safetyGate;
    private readonly TaskRouter _taskRouter;
    private readonly WorkflowTelemetryRecorder _telemetryRecorder;
    private readonly WorkspaceContextLeaseStore _workspaceContextLeases;

    public WorkflowKernel(
        CodeNavigationTools tools,
        EvidencePacketBuilder evidencePacketBuilder,
        WorkflowBudgetPolicy budgetPolicy,
        WorkflowSafetyGate safetyGate,
        TaskRouter taskRouter,
        WorkflowTelemetryRecorder telemetryRecorder,
        WorkspaceContextLeaseStore workspaceContextLeases)
    {
        _tools = tools;
        _evidencePacketBuilder = evidencePacketBuilder;
        _budgetPolicy = budgetPolicy;
        _safetyGate = safetyGate;
        _taskRouter = taskRouter;
        _telemetryRecorder = telemetryRecorder;
        _workspaceContextLeases = workspaceContextLeases;
    }

    public Task<WorkspaceQueryResult<CSharpEditTaskResult>> PrepareCSharpEditTaskAsync(
        CSharpEditTaskRequest request,
        CancellationToken cancellationToken)
    {
        var budget = _budgetPolicy.CreateDefaultEditBudget(request);
        return ExecuteWithDeadlineAsync(
            "prepare_csharp_edit_task",
            budget.MaxElapsedMilliseconds,
            cancellationToken,
            token => PrepareCSharpEditTaskCoreAsync(request, token));
    }

    private async Task<WorkspaceQueryResult<CSharpEditTaskResult>> PrepareCSharpEditTaskCoreAsync(
        CSharpEditTaskRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var phases = new WorkflowPhaseTracker();
        var taskId = CreateTaskId();
        TargetResolution targetResolution;
        using (phases.Measure("target-resolution"))
        {
            targetResolution = await ResolveTargetAsync(request.Target, request.WorkspaceContextLeaseId, cancellationToken).ConfigureAwait(false);
        }
        if (targetResolution.IsFailure)
        {
            return TargetFailure<CSharpEditTaskResult>(targetResolution);
        }
        request.Target = targetResolution.Target;
        WorkspaceQueryResult<CSharpTaskContextPackage> contextResult;
        using (phases.Measure("evidence-collection"))
        {
            contextResult = await _tools.GetCSharpTaskContext(
                request.ProblemText,
                request.BuildOutput,
                request.BuildLogFilePath,
                request.SymbolQuery,
                request.FilePath,
                request.IncludePathPatterns,
                request.ExcludePathPatterns,
                request.ChangedFiles,
                request.ProjectName,
                request.MinimumSeverity,
                request.NoiseProfile,
                request.CollectionMode,
                request.IncludeWholeSolutionDiagnostics,
                request.IncludeVisualStudioBuildOutput,
                request.MaxVisualStudioBuildOutputCharacters,
                request.MaxBuildIssues,
                request.MaxDiagnostics,
                request.MaxSymbols,
                request.MaxRelatedItems,
                request.MaxSourceSnippets,
                request.ContextLines,
                request.MaxCharsPerSnippet,
                request.IncludeGeneratedCode,
                request.Target?.PipeName,
                request.Target?.InstanceId,
                request.Target?.SolutionPath,
                cancellationToken)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();

        var diagnostics = targetResolution.Diagnostics.Concat(contextResult.Diagnostics).ToArray();
        var context = contextResult.Items.FirstOrDefault() ?? new CSharpTaskContextPackage
        {
            Status = "TaskContextUnavailable",
            SuggestedNextSteps = new[] { "Retry prepare_csharp_edit_task with a narrower filePath, symbolQuery, or explicit Visual Studio target." },
        };
        context.RecommendedNextActions = _taskRouter.RouteEditTask(request, context);
        var budget = _budgetPolicy.CreateDefaultEditBudget(request);
        var blockers = _safetyGate.EvaluateEditTask(context);
        var candidates = CreateEditCandidates(context);
        var telemetry = _telemetryRecorder.CreateTelemetry(
            "prepare_csharp_edit_task",
            stopwatch,
            candidates.Length + context.PrimaryDiagnostics.Length + context.PrimarySymbols.Length + context.PrimaryFiles.Length,
            EstimateReturnedCharacters(context),
            context.SourceSnippets.Length,
            contextResult.IsPartial,
            diagnostics);
        telemetry.PhaseTimings = phases.Snapshot();
        var packet = _evidencePacketBuilder.BuildEditTaskPacket(
            taskId,
            context,
            budget,
            telemetry,
            blockers,
            diagnostics,
            contextResult.IsPartial);

        var result = new CSharpEditTaskResult
        {
            Status = context.Status,
            TaskContext = context.TaskContext,
            EvidencePacket = packet,
            TaskContextPackage = request.DetailLevel == WorkflowResponseDetailLevel.Full ? context : null,
            EditCandidates = LimitForDetail(candidates, request.DetailLevel),
            RecommendedNextActions = context.RecommendedNextActions,
            SuggestedNextSteps = context.SuggestedNextSteps,
            WorkspaceContextLease = _workspaceContextLeases.Create(request.Target ?? new VisualStudioBridgeTarget(), context.TaskContext.TargetSolutionPath),
        };
        result.ResponseBudgetExceeded = ApplyResponseBudget(result, budget, request.DetailLevel);

        return new WorkspaceQueryResult<CSharpEditTaskResult>
        {
            Items = new[] { result },
            Diagnostics = LimitDiagnostics(diagnostics, budget),
            IsPartial = contextResult.IsPartial || result.ResponseBudgetExceeded,
        };
    }

    public Task<WorkspaceQueryResult<CSharpChangeReviewTaskResult>> PrepareCSharpChangeReviewAsync(
        CSharpChangeReviewTaskRequest request,
        CancellationToken cancellationToken)
    {
        var budget = _budgetPolicy.CreateDefaultChangeReviewBudget(request);
        return ExecuteWithDeadlineAsync(
            "prepare_csharp_change_review",
            budget.MaxElapsedMilliseconds,
            cancellationToken,
            token => PrepareCSharpChangeReviewCoreAsync(request, token));
    }

    private async Task<WorkspaceQueryResult<CSharpChangeReviewTaskResult>> PrepareCSharpChangeReviewCoreAsync(
        CSharpChangeReviewTaskRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var phases = new WorkflowPhaseTracker();
        var taskId = CreateTaskId();
        TargetResolution targetResolution;
        using (phases.Measure("target-resolution"))
        {
            targetResolution = await ResolveTargetAsync(request.Target, request.WorkspaceContextLeaseId, cancellationToken).ConfigureAwait(false);
        }
        if (targetResolution.IsFailure)
        {
            return TargetFailure<CSharpChangeReviewTaskResult>(targetResolution);
        }
        request.Target = targetResolution.Target;
        WorkspaceQueryResult<CSharpChangeReviewReport> reviewResult;
        using (phases.Measure("evidence-collection"))
        {
            reviewResult = await _tools.ReviewCSharpChange(
                request.ProblemText,
                request.BuildOutput,
                request.BuildLogFilePath,
                request.ChangedFiles,
                request.AreaPaths,
                request.SymbolQuery,
                request.ProjectName,
                request.IncludePathPatterns,
                request.ExcludePathPatterns,
                request.MinimumSeverity,
                request.NoiseProfile,
                request.CollectionMode,
                request.MaxBuildIssues,
                request.MaxDiagnostics,
                request.MaxSymbols,
                request.MaxRelatedItems,
                request.MaxFiles,
                request.IncludeGeneratedCode,
                request.Target?.PipeName,
                request.Target?.InstanceId,
                request.Target?.SolutionPath,
                cancellationToken)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();

        var diagnostics = targetResolution.Diagnostics.Concat(reviewResult.Diagnostics).ToArray();
        var review = reviewResult.Items.FirstOrDefault() ?? CreateUnavailableChangeReview(request);
        review.RecommendedNextActions = _taskRouter.RouteChangeReview(request, review);
        var budget = _budgetPolicy.CreateDefaultChangeReviewBudget(request);
        var blockers = _safetyGate.EvaluateChangeReview(review);
        var telemetry = _telemetryRecorder.CreateTelemetry(
            "prepare_csharp_change_review",
            stopwatch,
            review.PublicApiRisks.Length
                + review.TestGaps.Length
                + review.CandidateEditLocations.Length
                + review.PrimaryDiagnostics.Length
                + review.PrimarySymbols.Length,
            EstimateReturnedCharacters(review),
            1,
            reviewResult.IsPartial,
            diagnostics);
        telemetry.PhaseTimings = phases.Snapshot();
        var packet = _evidencePacketBuilder.BuildChangeReviewPacket(
            taskId,
            review,
            budget,
            telemetry,
            blockers,
            diagnostics,
            reviewResult.IsPartial);

        var result = new CSharpChangeReviewTaskResult
        {
            Status = review.Status,
            TaskContext = review.TaskContext,
            EvidencePacket = packet,
            ChangeReviewReport = request.DetailLevel == WorkflowResponseDetailLevel.Full ? review : null,
            PublicApiRisks = LimitForDetail(review.PublicApiRisks, request.DetailLevel),
            TestGaps = LimitForDetail(review.TestGaps, request.DetailLevel),
            CandidateEditLocations = LimitForDetail(review.CandidateEditLocations, request.DetailLevel),
            RecommendedNextActions = review.RecommendedNextActions,
            SuggestedNextSteps = review.SuggestedNextSteps,
            WorkspaceContextLease = _workspaceContextLeases.Create(request.Target ?? new VisualStudioBridgeTarget(), review.TaskContext.TargetSolutionPath),
        };
        result.ResponseBudgetExceeded = ApplyResponseBudget(result, budget, request.DetailLevel);

        return new WorkspaceQueryResult<CSharpChangeReviewTaskResult>
        {
            Items = new[] { result },
            Diagnostics = LimitDiagnostics(diagnostics, budget),
            IsPartial = reviewResult.IsPartial || result.ResponseBudgetExceeded,
        };
    }

    public Task<WorkspaceQueryResult<CSharpVerificationRunTaskResult>> PrepareCSharpVerificationRunAsync(
        CSharpVerificationRunTaskRequest request,
        CancellationToken cancellationToken)
    {
        var budget = _budgetPolicy.CreateDefaultVerificationRunBudget(request);
        return ExecuteWithDeadlineAsync(
            "prepare_csharp_verification_run",
            budget.MaxElapsedMilliseconds,
            cancellationToken,
            token => PrepareCSharpVerificationRunCoreAsync(request, token));
    }

    private async Task<WorkspaceQueryResult<CSharpVerificationRunTaskResult>> PrepareCSharpVerificationRunCoreAsync(
        CSharpVerificationRunTaskRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var phases = new WorkflowPhaseTracker();
        var taskId = CreateTaskId();
        TargetResolution targetResolution;
        using (phases.Measure("target-resolution"))
        {
            targetResolution = await ResolveTargetAsync(request.Target, request.WorkspaceContextLeaseId, cancellationToken).ConfigureAwait(false);
        }
        if (targetResolution.IsFailure)
        {
            return TargetFailure<CSharpVerificationRunTaskResult>(targetResolution);
        }
        request.Target = targetResolution.Target;
        WorkspaceQueryResult<CSharpRegressionScopePlan> planResult;
        using (phases.Measure("evidence-collection"))
        {
            planResult = await _tools.PlanCSharpRegressionScope(
                request.ProblemText,
                request.BuildOutput,
                request.BuildLogFilePath,
                request.ChangedFiles,
                request.AreaPaths,
                request.SymbolQuery,
                request.FilePath,
                request.IncludePathPatterns,
                request.ExcludePathPatterns,
                request.ProjectName,
                request.MinimumSeverity,
                request.NoiseProfile,
                request.CollectionMode,
                request.IncludeVisualStudioBuildOutput,
                request.MaxVisualStudioBuildOutputCharacters,
                request.MaxBuildIssues,
                request.MaxDiagnostics,
                request.MaxRelatedTests,
                request.MaxRelatedItems,
                request.IncludeGeneratedCode,
                request.Target?.PipeName,
                request.Target?.InstanceId,
                request.Target?.SolutionPath,
                cancellationToken)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();

        var diagnostics = targetResolution.Diagnostics.Concat(planResult.Diagnostics).ToArray();
        var plan = planResult.Items.FirstOrDefault() ?? CreateUnavailableVerificationPlan(request);
        plan.RecommendedNextActions = _taskRouter.RouteVerificationRun(request, plan);
        var budget = _budgetPolicy.CreateDefaultVerificationRunBudget(request);
        var blockers = _safetyGate.EvaluateVerificationRun(plan);
        var telemetry = _telemetryRecorder.CreateTelemetry(
            "prepare_csharp_verification_run",
            stopwatch,
            plan.SmokeCommands.Length
                + plan.FocusedCommands.Length
                + plan.BroadCommands.Length
                + plan.VerificationPlan.Diagnostics.Length
                + plan.VerificationPlan.RelatedTests.Length,
            EstimateReturnedCharacters(plan),
            1,
            planResult.IsPartial,
            diagnostics);
        telemetry.PhaseTimings = phases.Snapshot();
        var packet = _evidencePacketBuilder.BuildVerificationRunPacket(
            taskId,
            plan,
            budget,
            telemetry,
            blockers,
            diagnostics,
            planResult.IsPartial);

        var result = new CSharpVerificationRunTaskResult
        {
            Status = plan.Status,
            EvidencePacket = packet,
            RegressionScopePlan = request.DetailLevel == WorkflowResponseDetailLevel.Full ? plan : null,
            SmokeCommands = LimitForDetail(plan.SmokeCommands, request.DetailLevel),
            FocusedCommands = LimitForDetail(plan.FocusedCommands, request.DetailLevel),
            BroadCommands = LimitForDetail(plan.BroadCommands, request.DetailLevel),
            RecommendedNextActions = plan.RecommendedNextActions,
            SuggestedNextSteps = plan.SuggestedNextSteps,
            WorkspaceContextLease = _workspaceContextLeases.Create(request.Target ?? new VisualStudioBridgeTarget(), plan.VerificationPlan.TaskContext.TargetSolutionPath),
        };
        result.ResponseBudgetExceeded = ApplyResponseBudget(result, budget, request.DetailLevel);

        return new WorkspaceQueryResult<CSharpVerificationRunTaskResult>
        {
            Items = new[] { result },
            Diagnostics = LimitDiagnostics(diagnostics, budget),
            IsPartial = planResult.IsPartial || result.ResponseBudgetExceeded,
        };
    }

    public Task<WorkspaceQueryResult<CSharpRuntimeExceptionTaskResult>> InvestigateCSharpRuntimeExceptionAsync(
        CSharpRuntimeExceptionTaskRequest request,
        CancellationToken cancellationToken)
    {
        var budget = _budgetPolicy.CreateDefaultRuntimeExceptionBudget(request);
        return ExecuteWithDeadlineAsync(
            "investigate_csharp_runtime_exception",
            budget.MaxElapsedMilliseconds,
            cancellationToken,
            token => InvestigateCSharpRuntimeExceptionCoreAsync(request, token));
    }

    private async Task<WorkspaceQueryResult<CSharpRuntimeExceptionTaskResult>> InvestigateCSharpRuntimeExceptionCoreAsync(
        CSharpRuntimeExceptionTaskRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var phases = new WorkflowPhaseTracker();
        var taskId = CreateTaskId();
        TargetResolution targetResolution;
        using (phases.Measure("target-resolution"))
        {
            targetResolution = await ResolveTargetAsync(request.Target, request.WorkspaceContextLeaseId, cancellationToken).ConfigureAwait(false);
        }
        if (targetResolution.IsFailure)
        {
            return TargetFailure<CSharpRuntimeExceptionTaskResult>(targetResolution);
        }
        request.Target = targetResolution.Target;
        WorkspaceQueryResult<DebugSessionPreparationPlan> debugResult;
        using (phases.Measure("debug-evidence"))
        {
            debugResult = await _tools.PrepareDebugSession(
                request.MaxBreakpoints,
                request.MaxFrames,
                request.MaxSourceSnippets,
                request.ContextLines,
                request.MaxCharsPerSnippet,
                request.IncludeGeneratedCode,
                request.Target?.PipeName,
                request.Target?.InstanceId,
                request.Target?.SolutionPath,
                cancellationToken)
                .ConfigureAwait(false);
        }

        ArtifactEvidenceReport? artifactEvidence = null;
        var diagnostics = targetResolution.Diagnostics.Concat(debugResult.Diagnostics).ToList();
        var artifactPartial = false;
        if (request.ArtifactPaths.Length > 0)
        {
            using (phases.Measure("artifact-evidence"))
            {
                var artifactResult = await _tools.CollectArtifactEvidence(
                    request.ArtifactPaths,
                    rootDirectories: null,
                    searchPatterns: null,
                    request.IncludeTextPatterns,
                    excludePathPatterns: null,
                    maxArtifacts: 20,
                    maxLinesPerArtifact: 20,
                    maxCharsPerArtifact: 12000,
                    cancellationToken)
                    .ConfigureAwait(false);
                diagnostics.AddRange(artifactResult.Diagnostics.Select(diagnostic => "ArtifactEvidence: " + diagnostic));
                artifactEvidence = artifactResult.Items.FirstOrDefault();
                artifactPartial = artifactResult.IsPartial;
            }
        }

        stopwatch.Stop();

        var debugPlan = debugResult.Items.FirstOrDefault() ?? CreateUnavailableDebugSession();
        debugPlan.RecommendedNextActions = _taskRouter.RouteRuntimeException(request, debugPlan, artifactEvidence);
        var budget = _budgetPolicy.CreateDefaultRuntimeExceptionBudget(request);
        var blockers = _safetyGate.EvaluateRuntimeException(debugPlan);
        var telemetry = _telemetryRecorder.CreateTelemetry(
            "investigate_csharp_runtime_exception",
            stopwatch,
            debugPlan.CallStack.Length + debugPlan.SourceSnippets.Length + (artifactEvidence?.Artifacts.Length ?? 0),
            EstimateReturnedCharacters(debugPlan, artifactEvidence, request.ExceptionText),
            1,
            debugResult.IsPartial || artifactPartial,
            diagnostics);
        telemetry.PhaseTimings = phases.Snapshot();
        var packet = _evidencePacketBuilder.BuildRuntimeExceptionPacket(
            taskId,
            debugPlan,
            artifactEvidence,
            request.ExceptionText,
            budget,
            telemetry,
            blockers,
            diagnostics.ToArray(),
            debugResult.IsPartial || artifactPartial);

        var result = new CSharpRuntimeExceptionTaskResult
        {
            Status = debugPlan.Status,
            EvidencePacket = packet,
            DebugSession = request.DetailLevel == WorkflowResponseDetailLevel.Full ? debugPlan : null,
            ArtifactEvidence = request.DetailLevel == WorkflowResponseDetailLevel.Full ? artifactEvidence : null,
            CallStack = LimitForDetail(debugPlan.CallStack, request.DetailLevel),
            SourceSnippets = request.DetailLevel == WorkflowResponseDetailLevel.Full
                ? debugPlan.SourceSnippets
                : Array.Empty<SourceContextSnippet>(),
            RecommendedNextActions = packet.RecommendedNextActions,
            SuggestedNextSteps = debugPlan.SuggestedNextSteps
                .Concat(artifactEvidence?.SuggestedNextSteps ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            WorkspaceContextLease = _workspaceContextLeases.Create(request.Target ?? new VisualStudioBridgeTarget(), request.Target?.SolutionPath ?? string.Empty),
        };
        result.ResponseBudgetExceeded = ApplyResponseBudget(result, budget, request.DetailLevel);

        return new WorkspaceQueryResult<CSharpRuntimeExceptionTaskResult>
        {
            Items = new[] { result },
            Diagnostics = LimitDiagnostics(diagnostics, budget),
            IsPartial = debugResult.IsPartial || artifactPartial || result.ResponseBudgetExceeded,
        };
    }

    private static EditCandidate[] CreateEditCandidates(CSharpTaskContextPackage context)
    {
        var candidates = new List<EditCandidate>();

        candidates.AddRange(context.PrimarySymbols.Select(symbol => new EditCandidate
        {
            FilePath = symbol.Span?.FilePath ?? symbol.Symbol.Span?.FilePath ?? string.Empty,
            Span = symbol.Span ?? symbol.Symbol.Span,
            Symbol = symbol.Symbol,
            Score = symbol.Score,
            EvidenceLevel = symbol.EvidenceLevel,
            Reason = string.Join("; ", symbol.Reasons.Where(reason => !string.IsNullOrWhiteSpace(reason))),
            Risk = context.ImpactSummary is { HasCrossProjectImpact: true } ? "CrossProjectImpact" : "ScopedSymbol",
            VerificationHint = "Call plan_csharp_verification with changedFiles after editing.",
        }));

        candidates.AddRange(context.PrimaryFiles.Select(file => new EditCandidate
        {
            FilePath = file.FilePath,
            Span = file.Span,
            Score = file.Score,
            EvidenceLevel = file.EvidenceLevel,
            Reason = string.Join("; ", file.Reasons.Where(reason => !string.IsNullOrWhiteSpace(reason))),
            Risk = "FileScoped",
            VerificationHint = "Run focused diagnostics or project build for this file's project after editing.",
        }));

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.FilePath) || candidate.Symbol is not null)
            .OrderByDescending(candidate => candidate.Score)
            .Take(12)
            .ToArray();
    }

    private async Task<TargetResolution> ResolveTargetAsync(
        VisualStudioBridgeTarget? requestedTarget,
        string? leaseId,
        CancellationToken cancellationToken)
    {
        var target = requestedTarget ?? new VisualStudioBridgeTarget();
        if (_workspaceContextLeases.TryGet(leaseId, out var lease))
        {
            if (WorkspaceTargetIdentity.Conflicts(target, lease.Target))
            {
                return new TargetResolution(target,
                    new[] { "WorkspaceContextLeaseTargetMismatch: 显式目标与租约不一致，请移除旧租约后重新选择目标。" }, true);
            }

            var currentInstances = await _tools.ListVisualStudioInstances(includeStale: false, cancellationToken).ConfigureAwait(false);
            var current = currentInstances.Items.Where(instance => !instance.IsStale)
                .Where(instance => WorkspaceTargetIdentity.Matches(lease.Target, new VisualStudioBridgeTarget
                {
                    PipeName = instance.PipeName,
                    InstanceId = instance.InstanceId,
                    SolutionPath = instance.SolutionPath,
                }))
                .Where(instance => string.Equals(lease.SolutionPath.Replace('/', '\\'),
                    instance.SolutionPath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (currentInstances.IsPartial || lease.Target.IsEmpty || current.Length != 1)
            {
                return new TargetResolution(target,
                    new[] { "WorkspaceContextLeaseInvalidated: 原实例已不可用或解决方案已变化，请重新准备工作区。" }, true);
            }

            if (WorkspaceTargetIdentity.Matches(target, lease.Target))
            {
                return new TargetResolution(WorkspaceTargetIdentity.Copy(lease.Target), Array.Empty<string>());
            }
        }

        if (!string.IsNullOrWhiteSpace(target.PipeName))
        {
            return new TargetResolution(target, Array.Empty<string>());
        }

        var instances = await _tools.ListVisualStudioInstances(includeStale: false, cancellationToken).ConfigureAwait(false);
        var candidates = instances.Items
            .Where(instance => !instance.IsStale)
            .Where(instance => string.IsNullOrWhiteSpace(target.InstanceId)
                || string.Equals(instance.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase))
            .Where(instance => string.IsNullOrWhiteSpace(target.SolutionPath)
                || string.Equals(instance.SolutionPath, target.SolutionPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 1)
        {
            var candidate = candidates[0];
            return new TargetResolution(
                new VisualStudioBridgeTarget
                {
                    PipeName = candidate.PipeName,
                    InstanceId = candidate.InstanceId,
                    SolutionPath = candidate.SolutionPath,
                },
                Array.Empty<string>());
        }

        if (!string.IsNullOrWhiteSpace(leaseId))
        {
            return new TargetResolution(target, new[] { "WorkspaceContextLeaseExpired: target discovery was performed again." });
        }

        return new TargetResolution(target, Array.Empty<string>());
    }

    private static bool ApplyResponseBudget<T>(T result, WorkflowBudget budget, WorkflowResponseDetailLevel detailLevel)
        where T : class
    {
        if (detailLevel == WorkflowResponseDetailLevel.Compact)
        {
            TrimPacket(GetPacket(result), primaryLimit: 8, supportingLimit: 0);
        }
        else if (detailLevel == WorkflowResponseDetailLevel.Standard)
        {
            TrimPacket(GetPacket(result), primaryLimit: 12, supportingLimit: 4);
        }

        for (var pass = 0; pass < 4; pass++)
        {
            var serializedLength = JsonSerializer.Serialize(result).Length;
            GetPacket(result).Telemetry.ReturnedCharacterCount = serializedLength;
            if (serializedLength <= budget.MaxReturnedChars)
            {
                return false;
            }

            TrimPacket(
                GetPacket(result),
                primaryLimit: pass == 0 ? 4 : 2,
                supportingLimit: 0);
            SuppressDetailedPayload(result);
            if (pass >= 1)
            {
                TrimRecommendedText(result);
            }
        }

        GetPacket(result).Diagnostics = GetPacket(result).Diagnostics.Take(3)
            .Append("ResponseBudgetExceeded: detailed evidence is available from resourceLinks.")
            .ToArray();
        GetPacket(result).Telemetry.ReturnedCharacterCount = JsonSerializer.Serialize(result).Length;
        return true;
    }

    private static EvidencePacket GetPacket<T>(T result)
        where T : class
    {
        return result switch
        {
            CSharpEditTaskResult edit => edit.EvidencePacket,
            CSharpChangeReviewTaskResult review => review.EvidencePacket,
            CSharpVerificationRunTaskResult verification => verification.EvidencePacket,
            CSharpRuntimeExceptionTaskResult runtime => runtime.EvidencePacket,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private static void SuppressDetailedPayload<T>(T result)
        where T : class
    {
        switch (result)
        {
            case CSharpEditTaskResult edit:
                edit.TaskContextPackage = null;
                edit.EditCandidates = edit.EditCandidates.Take(3).ToArray();
                break;
            case CSharpChangeReviewTaskResult review:
                review.ChangeReviewReport = null;
                review.PublicApiRisks = review.PublicApiRisks.Take(3).ToArray();
                review.TestGaps = review.TestGaps.Take(3).ToArray();
                review.CandidateEditLocations = review.CandidateEditLocations.Take(3).ToArray();
                break;
            case CSharpVerificationRunTaskResult verification:
                verification.RegressionScopePlan = null;
                verification.SmokeCommands = verification.SmokeCommands.Take(3).ToArray();
                verification.FocusedCommands = verification.FocusedCommands.Take(3).ToArray();
                verification.BroadCommands = verification.BroadCommands.Take(3).ToArray();
                break;
            case CSharpRuntimeExceptionTaskResult runtime:
                runtime.DebugSession = null;
                runtime.ArtifactEvidence = null;
                runtime.CallStack = runtime.CallStack.Take(3).ToArray();
                runtime.SourceSnippets = Array.Empty<SourceContextSnippet>();
                break;
        }
    }

    private static void TrimPacket(EvidencePacket packet, int primaryLimit, int supportingLimit)
    {
        packet.PrimaryFindings = packet.PrimaryFindings.Take(primaryLimit).ToArray();
        packet.SupportingEvidence = packet.SupportingEvidence.Take(supportingLimit).ToArray();
        packet.RecommendedNextActions = packet.RecommendedNextActions.Take(4).ToArray();
        packet.SafetyBlockers = packet.SafetyBlockers.Take(4).ToArray();
        packet.ResidualRisks = packet.ResidualRisks.Take(4).ToArray();
        packet.Diagnostics = packet.Diagnostics.Take(8).ToArray();
        packet.ResourceLinks = packet.ResourceLinks
            .Take(4)
            .Select(link => new EvidenceResourceLink
            {
                Uri = link.Uri,
                Kind = link.Kind,
                Title = TrimText(link.Title, 160),
                Summary = TrimText(link.Summary, 320),
                IsPartial = link.IsPartial,
            })
            .ToArray();
    }

    private static void TrimRecommendedText<T>(T result)
        where T : class
    {
        switch (result)
        {
            case CSharpEditTaskResult edit:
                edit.SuggestedNextSteps = edit.SuggestedNextSteps.Take(3).Select(step => TrimText(step, 320)).ToArray();
                break;
            case CSharpChangeReviewTaskResult review:
                review.SuggestedNextSteps = review.SuggestedNextSteps.Take(3).Select(step => TrimText(step, 320)).ToArray();
                break;
            case CSharpVerificationRunTaskResult verification:
                verification.SuggestedNextSteps = verification.SuggestedNextSteps.Take(3).Select(step => TrimText(step, 320)).ToArray();
                break;
            case CSharpRuntimeExceptionTaskResult runtime:
                runtime.SuggestedNextSteps = runtime.SuggestedNextSteps.Take(3).Select(step => TrimText(step, 320)).ToArray();
                break;
        }
    }

    private static string TrimText(string value, int maxCharacters)
    {
        return value.Length <= maxCharacters
            ? value
            : value[..maxCharacters] + "...";
    }

    private static T[] LimitForDetail<T>(T[] items, WorkflowResponseDetailLevel detailLevel)
    {
        return detailLevel switch
        {
            WorkflowResponseDetailLevel.Compact => items.Take(3).ToArray(),
            WorkflowResponseDetailLevel.Standard => items.Take(8).ToArray(),
            _ => items,
        };
    }

    private static string[] LimitDiagnostics(IEnumerable<string> diagnostics, WorkflowBudget budget)
    {
        return diagnostics
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .Take(Math.Min(12, Math.Max(4, budget.MaxDiagnostics)))
            .ToArray();
    }

    private async Task<WorkspaceQueryResult<T>> ExecuteWithDeadlineAsync<T>(
        string workflowName,
        int maxElapsedMilliseconds,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<WorkspaceQueryResult<T>>> action)
        where T : class
    {
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCts.CancelAfter(TimeSpan.FromMilliseconds(maxElapsedMilliseconds));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await action(deadlineCts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            if (deadlineCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return AnnotateDeadline(result, workflowName, maxElapsedMilliseconds, stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (OperationCanceledException) when (
            deadlineCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new WorkspaceQueryResult<T>
            {
                IsPartial = true,
                Diagnostics = new[]
                {
                    $"WorkflowDeadlineExceeded: workflow={workflowName}; maxElapsedMilliseconds={maxElapsedMilliseconds}; elapsedMilliseconds={stopwatch.ElapsedMilliseconds}.",
                    "WorkflowDeadlineResultUnavailable: the workflow stopped before a stable task result was assembled; rerun with a narrower scope or a larger budget.",
                },
            };
        }
    }

    private static WorkspaceQueryResult<T> AnnotateDeadline<T>(
        WorkspaceQueryResult<T> result,
        string workflowName,
        int maxElapsedMilliseconds,
        long elapsedMilliseconds)
        where T : class
    {
        var item = result.Items.FirstOrDefault();
        if (item is not null)
        {
            var packet = item switch
            {
                CSharpEditTaskResult edit => edit.EvidencePacket,
                CSharpChangeReviewTaskResult review => review.EvidencePacket,
                CSharpVerificationRunTaskResult verification => verification.EvidencePacket,
                CSharpRuntimeExceptionTaskResult runtime => runtime.EvidencePacket,
                _ => null,
            };
            if (packet is not null)
            {
                packet.IsPartial = true;
                packet.Telemetry.DeadlineExceeded = true;
                packet.Telemetry.DeadlineMilliseconds = maxElapsedMilliseconds;
                packet.Telemetry.PhaseTimings = packet.Telemetry.PhaseTimings
                    .Append(new WorkflowPhaseTiming
                    {
                        Name = "deadline-guard",
                        ElapsedMilliseconds = elapsedMilliseconds,
                    })
                    .ToArray();
                packet.Diagnostics = packet.Diagnostics
                    .Append($"WorkflowDeadlineExceeded: workflow={workflowName}; maxElapsedMilliseconds={maxElapsedMilliseconds}.")
                    .Take(12)
                    .ToArray();
            }
        }

        result.IsPartial = true;
        result.Diagnostics = result.Diagnostics
            .Append($"WorkflowDeadlineExceeded: workflow={workflowName}; maxElapsedMilliseconds={maxElapsedMilliseconds}; elapsedMilliseconds={elapsedMilliseconds}.")
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        return result;
    }

    private static WorkspaceQueryResult<T> TargetFailure<T>(TargetResolution resolution) => new()
    {
        IsPartial = true,
        Succeeded = false,
        Diagnostics = resolution.Diagnostics,
    };

    private sealed record TargetResolution(VisualStudioBridgeTarget Target, string[] Diagnostics, bool IsFailure = false);

    private sealed class WorkflowPhaseTracker
    {
        private readonly List<WorkflowPhaseTiming> _timings = new();

        public IDisposable Measure(string name)
        {
            return new PhaseScope(name, _timings);
        }

        public WorkflowPhaseTiming[] Snapshot()
        {
            return _timings.ToArray();
        }

        private sealed class PhaseScope : IDisposable
        {
            private readonly string _name;
            private readonly List<WorkflowPhaseTiming> _timings;
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private bool _disposed;

            public PhaseScope(string name, List<WorkflowPhaseTiming> timings)
            {
                _name = name;
                _timings = timings;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stopwatch.Stop();
                _timings.Add(new WorkflowPhaseTiming
                {
                    Name = _name,
                    ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds,
                });
            }
        }
    }

    private static int EstimateReturnedCharacters(CSharpTaskContextPackage context)
    {
        return context.SourceSnippets.Sum(snippet => snippet.Text?.Length ?? 0)
            + context.SuggestedNextSteps.Sum(step => step.Length)
            + context.PrimaryDiagnostics.Sum(diagnostic => diagnostic.Message.Length);
    }

    private static int EstimateReturnedCharacters(CSharpChangeReviewReport review)
    {
        return review.PublicApiRisks.Sum(finding => finding.Title.Length + finding.Summary.Length)
            + review.TestGaps.Sum(finding => finding.Title.Length + finding.Summary.Length)
            + review.CandidateEditLocations.Sum(finding => finding.Title.Length + finding.Summary.Length)
            + review.PrimaryDiagnostics.Sum(diagnostic => diagnostic.Message.Length)
            + review.SuggestedNextSteps.Sum(step => step.Length);
    }

    private static int EstimateReturnedCharacters(CSharpRegressionScopePlan plan)
    {
        return plan.SmokeCommands.Sum(command => command.Command.Length)
            + plan.FocusedCommands.Sum(command => command.Command.Length)
            + plan.BroadCommands.Sum(command => command.Command.Length)
            + plan.VerificationPlan.Diagnostics.Sum(diagnostic => diagnostic.Message.Length)
            + plan.SuggestedNextSteps.Sum(step => step.Length);
    }

    private static int EstimateReturnedCharacters(DebugSessionPreparationPlan plan, ArtifactEvidenceReport? artifactEvidence, string exceptionText)
    {
        return plan.SourceSnippets.Sum(snippet => snippet.Text?.Length ?? 0)
            + plan.CallStack.Sum(frame => frame.FunctionName.Length + frame.ModuleName.Length)
            + (artifactEvidence?.Artifacts.Sum(artifact => artifact.Summary.Length) ?? 0)
            + exceptionText.Length
            + plan.SuggestedNextSteps.Sum(step => step.Length);
    }

    private static CSharpChangeReviewReport CreateUnavailableChangeReview(CSharpChangeReviewTaskRequest request)
    {
        return new CSharpChangeReviewReport
        {
            Status = "ChangeReviewUnavailable",
            TaskContext = new TaskContextSummary
            {
                ProblemText = request.ProblemText,
                ProjectName = request.ProjectName ?? string.Empty,
                SymbolQuery = request.SymbolQuery ?? string.Empty,
                ChangedFiles = request.ChangedFiles,
                IncludePathPatterns = request.IncludePathPatterns,
                ExcludePathPatterns = request.ExcludePathPatterns,
                TargetSolutionPath = request.Target?.SolutionPath ?? string.Empty,
                TargetInstanceId = request.Target?.InstanceId ?? string.Empty,
                EvidenceLevel = WorkflowEvidenceLevel.Background,
            },
            EvidenceLevel = WorkflowEvidenceLevel.Background,
            SuggestedNextSteps = new[] { "Retry prepare_csharp_change_review with changedFiles, areaPaths, symbolQuery, or an explicit Visual Studio target." },
        };
    }

    private static CSharpRegressionScopePlan CreateUnavailableVerificationPlan(CSharpVerificationRunTaskRequest request)
    {
        return new CSharpRegressionScopePlan
        {
            Status = "VerificationRunUnavailable",
            VerificationPlan = new CSharpVerificationPlan
            {
                Status = "VerificationUnavailable",
                TaskContext = new TaskContextSummary
                {
                    ProblemText = request.ProblemText,
                    ProjectName = request.ProjectName ?? string.Empty,
                    FilePath = request.FilePath ?? string.Empty,
                    SymbolQuery = request.SymbolQuery ?? string.Empty,
                    ChangedFiles = request.ChangedFiles,
                    IncludePathPatterns = request.IncludePathPatterns,
                    ExcludePathPatterns = request.ExcludePathPatterns,
                    TargetSolutionPath = request.Target?.SolutionPath ?? string.Empty,
                    TargetInstanceId = request.Target?.InstanceId ?? string.Empty,
                    EvidenceLevel = WorkflowEvidenceLevel.Background,
                },
                EvidenceLevel = WorkflowEvidenceLevel.Background,
                ChangedFiles = request.ChangedFiles,
                SuggestedNextSteps = new[] { "Retry prepare_csharp_verification_run with changedFiles, filePath, symbolQuery, projectName, or an explicit Visual Studio target." },
            },
            SuggestedNextSteps = new[] { "Retry prepare_csharp_verification_run with changedFiles, filePath, symbolQuery, projectName, or an explicit Visual Studio target." },
        };
    }

    private static DebugSessionPreparationPlan CreateUnavailableDebugSession()
    {
        return new DebugSessionPreparationPlan
        {
            Status = "DebugSessionUnavailable",
            SuggestedNextSteps = new[] { "Retry investigate_csharp_runtime_exception with an explicit Visual Studio target after opening the solution." },
        };
    }

    private static string CreateTaskId()
    {
        return $"task-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 38);
    }
}
