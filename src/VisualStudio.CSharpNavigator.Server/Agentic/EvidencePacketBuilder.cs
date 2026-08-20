using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Agentic;

public sealed class EvidencePacketBuilder
{
    private readonly EvidenceStore _evidenceStore;

    public EvidencePacketBuilder(EvidenceStore evidenceStore)
    {
        _evidenceStore = evidenceStore;
    }

    public EvidencePacket BuildEditTaskPacket(
        string taskId,
        CSharpTaskContextPackage context,
        WorkflowBudget budget,
        WorkflowTelemetry telemetry,
        SafetyBlocker[] safetyBlockers,
        string[] diagnostics,
        bool isPartial)
    {
        var primary = new List<EvidenceItem>();
        primary.AddRange(context.PrimaryDiagnostics.Select((diagnostic, index) => new EvidenceItem
        {
            Id = $"diagnostic-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.RoslynDiagnostic,
            EvidenceLevel = diagnostic.EvidenceLevel,
            Summary = $"{diagnostic.Id}: {diagnostic.Message}",
            SourceRef = diagnostic.Span?.FilePath ?? diagnostic.ProjectName,
            Span = diagnostic.Span,
            ProjectName = diagnostic.ProjectName,
            RelevanceReasons = diagnostic.Reasons,
            Confidence = diagnostic.IsLikelyCascade ? "medium" : "high",
            IsPartial = false,
        }));

        primary.AddRange(context.PrimarySymbols.Select((symbol, index) => new EvidenceItem
        {
            Id = $"symbol-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.Symbol,
            EvidenceLevel = symbol.EvidenceLevel,
            Summary = FormatSymbolName(symbol.Symbol),
            SourceRef = symbol.Span?.FilePath ?? symbol.Symbol.ProjectName,
            Span = symbol.Span,
            Symbol = symbol.Symbol,
            ProjectName = symbol.Symbol.ProjectName,
            RelevanceReasons = symbol.Reasons,
            Confidence = "medium",
            IsPartial = false,
        }));

        primary.AddRange(context.PrimaryFiles.Select((file, index) => new EvidenceItem
        {
            Id = $"file-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.SourceSpan,
            EvidenceLevel = file.EvidenceLevel,
            Summary = file.FilePath,
            SourceRef = file.FilePath,
            Span = file.Span,
            ProjectName = file.ProjectName,
            RelevanceReasons = file.Reasons,
            Confidence = "medium",
            IsPartial = false,
        }));

        var supporting = context.SourceSnippets.Select((snippet, index) => new EvidenceItem
        {
            Id = $"snippet-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.SourceSnippet,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = snippet.Symbol is null ? snippet.FilePath : FormatSymbolName(snippet.Symbol),
            SourceRef = snippet.FilePath,
            Span = snippet.FocusSpan,
            ProjectName = snippet.ProjectName,
            RelevanceReasons = snippet.Reasons,
            Confidence = "high",
            IsPartial = snippet.IsTextTruncated,
        }).ToArray();

        var resourceLinks = CreateResourceLinks(taskId, context);

        return new EvidencePacket
        {
            TaskId = taskId,
            TaskKind = AgentWorkflowTaskKind.EditTask,
            EvidenceLevel = context.EvidenceLevel,
            PrimaryFindings = primary
                .OrderByDescending(item => item.EvidenceLevel == WorkflowEvidenceLevel.Fact ? 3 : item.EvidenceLevel == WorkflowEvidenceLevel.Inference ? 2 : 1)
                .Take(20)
                .ToArray(),
            SupportingEvidence = supporting,
            ResourceLinks = resourceLinks,
            RecommendedNextActions = context.RecommendedNextActions,
            SafetyBlockers = safetyBlockers,
            ResidualRisks = CreateResidualRisks(context, isPartial),
            Budget = budget,
            Telemetry = telemetry,
            IsPartial = isPartial,
            Diagnostics = diagnostics,
        };
    }

    public EvidencePacket BuildChangeReviewPacket(
        string taskId,
        CSharpChangeReviewReport review,
        WorkflowBudget budget,
        WorkflowTelemetry telemetry,
        SafetyBlocker[] safetyBlockers,
        string[] diagnostics,
        bool isPartial)
    {
        var primary = new List<EvidenceItem>();

        primary.AddRange(review.PublicApiRisks.Select((finding, index) => CreateFindingEvidenceItem($"public-api-risk-{index + 1}", finding)));
        primary.AddRange(review.TestGaps.Select((finding, index) => CreateFindingEvidenceItem($"test-gap-{index + 1}", finding)));
        primary.AddRange(review.PrimaryDiagnostics.Select((diagnostic, index) => new EvidenceItem
        {
            Id = $"diagnostic-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.RoslynDiagnostic,
            EvidenceLevel = diagnostic.EvidenceLevel,
            Summary = $"{diagnostic.Id}: {diagnostic.Message}",
            SourceRef = diagnostic.Span?.FilePath ?? diagnostic.ProjectName,
            Span = diagnostic.Span,
            ProjectName = diagnostic.ProjectName,
            RelevanceReasons = diagnostic.Reasons,
            Confidence = diagnostic.IsLikelyCascade ? "medium" : "high",
            IsPartial = false,
        }));

        primary.AddRange(review.PrimarySymbols.Select((symbol, index) => new EvidenceItem
        {
            Id = $"symbol-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.Symbol,
            EvidenceLevel = symbol.EvidenceLevel,
            Summary = FormatSymbolName(symbol.Symbol),
            SourceRef = symbol.Span?.FilePath ?? symbol.Symbol.ProjectName,
            Span = symbol.Span,
            Symbol = symbol.Symbol,
            ProjectName = symbol.Symbol.ProjectName,
            RelevanceReasons = symbol.Reasons,
            Confidence = "medium",
            IsPartial = false,
        }));

        var supporting = new List<EvidenceItem>();
        supporting.AddRange(review.Findings.Select((finding, index) => CreateFindingEvidenceItem($"finding-{index + 1}", finding)));
        supporting.AddRange(review.CandidateEditLocations.Select((finding, index) => CreateFindingEvidenceItem($"candidate-edit-location-{index + 1}", finding)));
        supporting.AddRange(review.TemporaryMarkers.Select((marker, index) => new EvidenceItem
        {
            Id = $"temporary-marker-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.SourceSpan,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = $"{marker.Marker}: {marker.Text}",
            SourceRef = marker.Span?.FilePath ?? marker.ProjectName,
            Span = marker.Span,
            ProjectName = marker.ProjectName,
            RelevanceReasons = new[] { "temporary marker" },
            Confidence = "high",
            IsPartial = false,
        }));

        var resourceLinks = CreateChangeReviewResourceLinks(taskId, review);

        return new EvidencePacket
        {
            TaskId = taskId,
            TaskKind = AgentWorkflowTaskKind.ChangeReview,
            EvidenceLevel = review.EvidenceLevel,
            PrimaryFindings = primary
                .OrderByDescending(item => item.EvidenceLevel == WorkflowEvidenceLevel.Fact ? 3 : item.EvidenceLevel == WorkflowEvidenceLevel.Inference ? 2 : 1)
                .Take(20)
                .ToArray(),
            SupportingEvidence = supporting.Take(40).ToArray(),
            ResourceLinks = resourceLinks,
            RecommendedNextActions = review.RecommendedNextActions,
            SafetyBlockers = safetyBlockers,
            ResidualRisks = CreateReviewResidualRisks(review, isPartial),
            Budget = budget,
            Telemetry = telemetry,
            IsPartial = isPartial,
            Diagnostics = diagnostics,
        };
    }

    public EvidencePacket BuildVerificationRunPacket(
        string taskId,
        CSharpRegressionScopePlan plan,
        WorkflowBudget budget,
        WorkflowTelemetry telemetry,
        SafetyBlocker[] safetyBlockers,
        string[] diagnostics,
        bool isPartial)
    {
        var primary = new List<EvidenceItem>();
        primary.AddRange(plan.SmokeCommands.Select((command, index) => CreateCommandEvidenceItem($"smoke-command-{index + 1}", command)));
        primary.AddRange(plan.FocusedCommands.Select((command, index) => CreateCommandEvidenceItem($"focused-command-{index + 1}", command)));
        primary.AddRange(plan.VerificationPlan.Diagnostics.Take(10).Select((diagnostic, index) => new EvidenceItem
        {
            Id = $"diagnostic-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.RoslynDiagnostic,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = $"{diagnostic.Id}: {diagnostic.Message}",
            SourceRef = diagnostic.Span?.FilePath ?? diagnostic.ProjectName,
            Span = diagnostic.Span,
            ProjectName = diagnostic.ProjectName,
            RelevanceReasons = diagnostic.ScopeReasons,
            Confidence = "high",
            IsPartial = false,
        }));

        var supporting = plan.BroadCommands
            .Select((command, index) => CreateCommandEvidenceItem($"broad-command-{index + 1}", command))
            .ToArray();
        var resourceLinks = CreateVerificationResourceLinks(taskId, plan);

        return new EvidencePacket
        {
            TaskId = taskId,
            TaskKind = AgentWorkflowTaskKind.Verification,
            EvidenceLevel = plan.VerificationPlan.EvidenceLevel,
            PrimaryFindings = primary.Take(20).ToArray(),
            SupportingEvidence = supporting,
            ResourceLinks = resourceLinks,
            RecommendedNextActions = plan.RecommendedNextActions,
            SafetyBlockers = safetyBlockers,
            ResidualRisks = CreateVerificationResidualRisks(plan, isPartial),
            Budget = budget,
            Telemetry = telemetry,
            IsPartial = isPartial,
            Diagnostics = diagnostics,
        };
    }

    public EvidencePacket BuildRuntimeExceptionPacket(
        string taskId,
        DebugSessionPreparationPlan plan,
        ArtifactEvidenceReport? artifactEvidence,
        string exceptionText,
        WorkflowBudget budget,
        WorkflowTelemetry telemetry,
        SafetyBlocker[] safetyBlockers,
        string[] diagnostics,
        bool isPartial)
    {
        var primary = new List<EvidenceItem>();
        if (plan.DebuggerStatus?.CurrentFrame is not null)
        {
            primary.Add(CreateDebugFrameEvidenceItem("current-frame", plan.DebuggerStatus.CurrentFrame));
        }

        primary.AddRange(plan.CallStack.Take(12).Select((frame, index) => CreateDebugFrameEvidenceItem($"frame-{index + 1}", frame)));

        if (!string.IsNullOrWhiteSpace(exceptionText))
        {
            primary.Add(new EvidenceItem
            {
                Id = "exception-text",
                Kind = AgentWorkflowEvidenceKind.Artifact,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Summary = exceptionText.Length > 500 ? exceptionText.Substring(0, 500) : exceptionText,
                SourceRef = "request.exceptionText",
                RelevanceReasons = new[] { "user supplied runtime exception text" },
                Confidence = "high",
                IsPartial = exceptionText.Length > 500,
            });
        }

        var supporting = new List<EvidenceItem>();
        supporting.AddRange(plan.SourceSnippets.Select((snippet, index) => new EvidenceItem
        {
            Id = $"snippet-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.SourceSnippet,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = snippet.Symbol is null ? snippet.FilePath : FormatSymbolName(snippet.Symbol),
            SourceRef = snippet.FilePath,
            Span = snippet.FocusSpan,
            ProjectName = snippet.ProjectName,
            RelevanceReasons = snippet.Reasons,
            Confidence = "high",
            IsPartial = snippet.IsTextTruncated,
        }));

        if (artifactEvidence is not null)
        {
            supporting.AddRange(artifactEvidence.Artifacts.Select((artifact, index) => new EvidenceItem
            {
                Id = $"artifact-{index + 1}",
                Kind = AgentWorkflowEvidenceKind.Artifact,
                EvidenceLevel = WorkflowEvidenceLevel.Fact,
                Summary = artifact.Summary,
                SourceRef = artifact.FilePath,
                RelevanceReasons = artifact.MatchedPatterns,
                Confidence = "high",
                IsPartial = artifact.IsTruncated,
            }));
        }

        var resourceLinks = CreateRuntimeResourceLinks(taskId, plan, artifactEvidence, exceptionText);

        return new EvidencePacket
        {
            TaskId = taskId,
            TaskKind = AgentWorkflowTaskKind.RuntimeDebug,
            EvidenceLevel = plan.DebuggerStatus?.IsPaused == true ? WorkflowEvidenceLevel.Fact : WorkflowEvidenceLevel.Inference,
            PrimaryFindings = primary.Take(20).ToArray(),
            SupportingEvidence = supporting.Take(40).ToArray(),
            ResourceLinks = resourceLinks,
            RecommendedNextActions = plan.RecommendedNextActions.Concat(artifactEvidence?.RecommendedNextActions ?? Array.Empty<RecommendedNextAction>()).ToArray(),
            SafetyBlockers = safetyBlockers,
            ResidualRisks = CreateRuntimeResidualRisks(plan, artifactEvidence, isPartial),
            Budget = budget,
            Telemetry = telemetry,
            IsPartial = isPartial,
            Diagnostics = diagnostics,
        };
    }

    public EvidencePacket BuildBuildFailurePacket(
        string taskId,
        CSharpBuildFailureContext context,
        BuildFailureSession session,
        WorkflowBudget budget,
        WorkflowTelemetry telemetry,
        SafetyBlocker[] safetyBlockers,
        string[] diagnostics,
        bool isPartial)
    {
        var primary = new List<EvidenceItem>();
        primary.AddRange(session.RootCauseCandidates.Select((issue, index) => CreateBuildIssueEvidenceItem($"root-cause-{index + 1}", issue)));
        primary.AddRange(session.ScopedDiagnostics.Take(10).Select((diagnostic, index) => new EvidenceItem
        {
            Id = $"diagnostic-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.RoslynDiagnostic,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = $"{diagnostic.Id}: {diagnostic.Message}",
            SourceRef = diagnostic.Span?.FilePath ?? diagnostic.ProjectName,
            Span = diagnostic.Span,
            ProjectName = diagnostic.ProjectName,
            RelevanceReasons = diagnostic.ScopeReasons,
            Confidence = "high",
            IsPartial = false,
        }));

        var supporting = new List<EvidenceItem>();
        supporting.AddRange(session.CascadeIssues.Select((issue, index) => CreateBuildIssueEvidenceItem($"cascade-{index + 1}", issue)));
        supporting.AddRange(session.SourceSnippets.Select((snippet, index) => new EvidenceItem
        {
            Id = $"snippet-{index + 1}",
            Kind = AgentWorkflowEvidenceKind.SourceSnippet,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = snippet.Symbol is null ? snippet.FilePath : FormatSymbolName(snippet.Symbol),
            SourceRef = snippet.FilePath,
            Span = snippet.FocusSpan,
            ProjectName = snippet.ProjectName,
            RelevanceReasons = snippet.Reasons,
            Confidence = "high",
            IsPartial = snippet.IsTextTruncated,
        }));

        var resourceLinks = CreateBuildFailureResourceLinks(taskId, context, session);

        return new EvidencePacket
        {
            TaskId = taskId,
            TaskKind = AgentWorkflowTaskKind.BuildFailure,
            EvidenceLevel = context.EvidenceLevel,
            PrimaryFindings = primary.Take(20).ToArray(),
            SupportingEvidence = supporting.Take(40).ToArray(),
            ResourceLinks = resourceLinks,
            RecommendedNextActions = session.RecommendedNextActions,
            SafetyBlockers = safetyBlockers,
            ResidualRisks = CreateBuildFailureResidualRisks(context, session, isPartial),
            Budget = budget,
            Telemetry = telemetry,
            IsPartial = isPartial,
            Diagnostics = diagnostics,
        };
    }

    private static string[] CreateResidualRisks(CSharpTaskContextPackage context, bool isPartial)
    {
        var risks = new List<string>();
        if (isPartial)
        {
            risks.Add("当前证据包存在 partial 或截断，扩大修改范围前需要补充上下文。");
        }

        if (context.SourceSnippets.Any(snippet => snippet.IsTextTruncated))
        {
            risks.Add("部分源码片段被截断，必要时应按返回 span 读取更窄范围。");
        }

        if (context.PrimarySymbols.Length == 0 && string.IsNullOrWhiteSpace(context.TaskContext.SymbolQuery) == false)
        {
            risks.Add("符号查询没有形成主候选，后续可能需要更精确的 symbolQuery 或 filePath。");
        }

        return risks.ToArray();
    }

    private EvidenceResourceLink[] CreateResourceLinks(string taskId, CSharpTaskContextPackage context)
    {
        var links = new List<EvidenceResourceLink>
        {
            CreateContextResourceLink(taskId, context),
            CreateContextJsonResourceLink(taskId, context),
        };
        links.AddRange(CreateSourceResourceLinks(taskId, context.SourceSnippets));
        return links.ToArray();
    }

    private EvidenceResourceLink CreateJsonResourceLink(
        string uri,
        string name,
        string title,
        string description,
        string kind,
        string summary,
        object payload,
        bool isPartial)
    {
        _evidenceStore.AddJsonResource(
            uri,
            name,
            title,
            description,
            payload,
            isPartial);

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = kind,
            Title = title,
            Summary = summary,
            IsPartial = isPartial,
        };
    }

    private EvidenceResourceLink[] CreateChangeReviewResourceLinks(string taskId, CSharpChangeReviewReport review)
    {
        var links = new List<EvidenceResourceLink>();
        var uri = $"csharp://task/{taskId}/review";
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {review.Status}",
                $"Problem: {review.TaskContext.ProblemText}",
                $"Changed files: {string.Join(", ", review.TaskContext.ChangedFiles)}",
                $"Symbol: {review.TaskContext.SymbolQuery}",
                $"Project: {review.TaskContext.ProjectName}",
                $"Public API risks: {review.PublicApiRisks.Length}",
                $"Test gaps: {review.TestGaps.Length}",
                $"Candidate edit locations: {review.CandidateEditLocations.Length}",
                $"Temporary markers: {review.TemporaryMarkers.Length}",
                $"Diagnostics: {review.PrimaryDiagnostics.Length}",
            });

        _evidenceStore.AddTextResource(
            uri,
            "change-review",
            "Change review summary",
            "Compact Agentic C# workflow change review.",
            "text/plain",
            text,
            false);

        links.Add(
            new EvidenceResourceLink
            {
                Uri = uri,
                Kind = "review",
                Title = "Change review summary",
                Summary = review.Status,
                IsPartial = false,
            });
        links.Add(CreateJsonResourceLink(
            $"csharp://task/{taskId}/review.json",
            "change-review-json",
            "Change review JSON",
            "Machine-readable Agentic C# workflow change review.",
            "review-json",
            review.Status,
            new
            {
                review.Status,
                review.TaskContext,
                review.EvidenceLevel,
                review.PublicApiRisks,
                review.TestGaps,
                review.CandidateEditLocations,
                review.TemporaryMarkers,
                review.ImpactSummary,
                review.RecommendedNextActions,
                review.SuggestedNextSteps,
            },
            false));

        if (review.ImpactSummary is not null)
        {
            links.Add(CreateImpactResourceLink(taskId, review.ImpactSummary));
        }

        if (review.Diagnostics.Length > 0)
        {
            links.Add(CreateDiagnosticsResourceLink(taskId, review.Diagnostics, "review-diagnostics"));
        }

        return links.ToArray();
    }

    private EvidenceResourceLink CreateImpactResourceLink(string taskId, SymbolImpactSummary impact)
    {
        var uri = $"csharp://task/{taskId}/impact-graph";
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Symbol: {FormatSymbolName(impact.Symbol)}",
                $"Total references: {impact.TotalReferences}",
                $"Distinct files: {impact.DistinctFileCount}",
                $"Distinct projects: {impact.DistinctProjectCount}",
                $"Cross-project impact: {impact.HasCrossProjectImpact}",
                $"Max depth: {impact.MaxDepth}",
            });

        _evidenceStore.AddTextResource(
            uri,
            "impact-graph",
            "Symbol impact graph",
            "Compact Agentic C# workflow symbol impact summary.",
            "text/plain",
            text,
            false);

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = "impact-graph",
            Title = "Symbol impact graph",
            Summary = FormatSymbolName(impact.Symbol),
            IsPartial = false,
        };
    }

    private EvidenceResourceLink[] CreateVerificationResourceLinks(string taskId, CSharpRegressionScopePlan plan)
    {
        var uri = $"csharp://task/{taskId}/verification";
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {plan.Status}",
                $"Verification status: {plan.VerificationPlan.Status}",
                $"Changed files: {string.Join(", ", plan.VerificationPlan.ChangedFiles)}",
                $"Smoke commands: {plan.SmokeCommands.Length}",
                $"Focused commands: {plan.FocusedCommands.Length}",
                $"Broad commands: {plan.BroadCommands.Length}",
                $"Diagnostics: {plan.VerificationPlan.Diagnostics.Length}",
                $"Related tests: {plan.VerificationPlan.RelatedTests.Length}",
            });

        _evidenceStore.AddTextResource(
            uri,
            "verification-run",
            "Verification run summary",
            "Compact Agentic C# workflow verification run plan.",
            "text/plain",
            text,
            false);

        return new[]
        {
            new EvidenceResourceLink
            {
                Uri = uri,
                Kind = "verification",
                Title = "Verification run summary",
                Summary = plan.Status,
                IsPartial = false,
            },
            CreateJsonResourceLink(
                $"csharp://task/{taskId}/verification.json",
                "verification-run-json",
                "Verification run JSON",
                "Machine-readable Agentic C# workflow verification run plan.",
                "verification-json",
                plan.Status,
                new
                {
                    plan.Status,
                    plan.VerificationPlan,
                    plan.SmokeCommands,
                    plan.FocusedCommands,
                    plan.BroadCommands,
                    plan.RecommendedNextActions,
                    plan.SuggestedNextSteps,
                },
                false),
        };
    }

    private EvidenceResourceLink[] CreateRuntimeResourceLinks(
        string taskId,
        DebugSessionPreparationPlan plan,
        ArtifactEvidenceReport? artifactEvidence,
        string exceptionText)
    {
        var links = new List<EvidenceResourceLink>();
        var uri = $"csharp://task/{taskId}/debug-evidence";
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {plan.Status}",
                $"Debugger state: {plan.DebuggerStatus?.State.ToString() ?? "Unknown"}",
                $"Is debugging: {plan.DebuggerStatus?.IsDebugging.ToString() ?? "False"}",
                $"Is paused: {plan.DebuggerStatus?.IsPaused.ToString() ?? "False"}",
                $"Current frame: {plan.DebuggerStatus?.CurrentFrame?.FunctionName ?? string.Empty}",
                $"Call stack frames: {plan.CallStack.Length}",
                $"Source snippets: {plan.SourceSnippets.Length}",
                $"Artifacts: {artifactEvidence?.Artifacts.Length ?? 0}",
                $"Exception text: {exceptionText}",
            });

        _evidenceStore.AddTextResource(
            uri,
            "debug-evidence",
            "Runtime exception debug evidence",
            "Compact Agentic C# workflow runtime exception evidence.",
            "text/plain",
            text,
            false);

        links.Add(
            new EvidenceResourceLink
            {
                Uri = uri,
                Kind = "debug-evidence",
                Title = "Runtime exception debug evidence",
                Summary = plan.Status,
                IsPartial = false,
            });
        links.Add(CreateJsonResourceLink(
            $"csharp://task/{taskId}/debug-evidence.json",
            "debug-evidence-json",
            "Runtime exception debug evidence JSON",
            "Machine-readable Agentic C# workflow runtime exception evidence.",
            "debug-evidence-json",
            plan.Status,
            new
            {
                plan.Status,
                plan.DebuggerStatus,
                plan.CallStack,
                plan.SourceSnippets,
                ArtifactEvidence = artifactEvidence,
                ExceptionText = exceptionText,
                plan.RecommendedNextActions,
                plan.SuggestedNextSteps,
            },
            false));

        if (artifactEvidence is not null && artifactEvidence.Artifacts.Length > 0)
        {
            links.Add(CreateArtifactResourceLink(taskId, artifactEvidence));
        }

        return links.ToArray();
    }

    private EvidenceResourceLink CreateArtifactResourceLink(string taskId, ArtifactEvidenceReport artifactEvidence)
    {
        var uri = $"csharp://task/{taskId}/artifact/1";
        var lines = new List<string>
        {
            $"Status: {artifactEvidence.Status}",
            $"Artifacts: {artifactEvidence.Artifacts.Length}",
        };
        lines.AddRange(artifactEvidence.Artifacts.Select(artifact => $"{artifact.Kind}: {artifact.FilePath} | {artifact.Summary}"));

        _evidenceStore.AddTextResource(
            uri,
            "artifact-1",
            "Artifact evidence summary",
            "Compact Agentic C# workflow artifact evidence.",
            "text/plain",
            string.Join(Environment.NewLine, lines),
            artifactEvidence.Artifacts.Any(artifact => artifact.IsTruncated));

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = "artifact",
            Title = "Artifact evidence summary",
            Summary = artifactEvidence.Status,
            IsPartial = artifactEvidence.Artifacts.Any(artifact => artifact.IsTruncated),
        };
    }

    private EvidenceResourceLink[] CreateBuildFailureResourceLinks(
        string taskId,
        CSharpBuildFailureContext context,
        BuildFailureSession session)
    {
        var links = new List<EvidenceResourceLink>();
        var uri = $"csharp://task/{taskId}/build-failure";
        var lines = new List<string>
        {
            $"Status: {context.Status}",
            $"Problem: {context.TaskContext.ProblemText}",
            $"Build evidence source: {context.TaskContext.BuildEvidenceSource}",
            $"Root-cause candidates: {session.RootCauseCandidates.Length}",
            $"Cascade issues: {session.CascadeIssues.Length}",
            $"Issue bindings: {session.IssueBindings.Length}",
            $"Project bindings: {session.ProjectBindings.Length}",
            $"Related tests: {session.RelatedTests.Length}",
            $"Recommended commands: {session.RecommendedCommands.Length}",
            $"Scoped diagnostics: {session.ScopedDiagnostics.Length}",
            $"Source snippets: {session.SourceSnippets.Length}",
            $"Error List items: {session.ErrorListItems.Length}",
            $"Build output window: {(session.BuildOutputWindow is null ? "None" : session.BuildOutputWindow.PaneName)}",
        };
        lines.AddRange(session.ProjectBindings.Select(project =>
            $"Project: {project.ProjectName} issues={string.Join(",", project.IssueIds)} confidence={project.Confidence}"));
        lines.AddRange(session.IssueBindings.Select(binding =>
            $"IssueBinding: {binding.IssueId} project={binding.BoundProjectName} symbol={FormatOptionalSymbolName(binding.EnclosingSymbol)} tests={binding.RelatedTests.Length} confidence={binding.Confidence}"));
        lines.AddRange(session.RecommendedCommands.Select(command =>
            $"Command: {command.Scope} confidence={command.Confidence} {command.Command}"));

        _evidenceStore.AddTextResource(
            uri,
            "build-failure",
            "Build failure session",
            "Compact Agentic C# workflow build failure evidence.",
            "text/plain",
            string.Join(Environment.NewLine, lines),
            false);

        links.Add(
            new EvidenceResourceLink
            {
                Uri = uri,
                Kind = "build-failure",
                Title = "Build failure session",
                Summary = context.Status,
                IsPartial = false,
            });
        links.Add(CreateJsonResourceLink(
            $"csharp://task/{taskId}/build-failure.json",
            "build-failure-json",
            "Build failure session JSON",
            "Machine-readable Agentic C# workflow build failure evidence.",
            "build-failure-json",
            context.Status,
            new
            {
                context.Status,
                context.TaskContext,
                BuildTriage = context.BuildTriage,
                Session = session,
                context.RecommendedNextActions,
                context.SuggestedNextSteps,
            },
            false));

        if (context.BuildTriage?.Issues.Length > 0)
        {
            links.Add(CreateBuildLogResourceLink(taskId, context.BuildTriage));
        }

        if (context.Diagnostics.Length > 0)
        {
            links.Add(CreateDiagnosticsResourceLink(taskId, context.Diagnostics, "build-diagnostics"));
        }

        return links.ToArray();
    }

    private EvidenceResourceLink CreateBuildLogResourceLink(string taskId, BuildTriageReport buildTriage)
    {
        var uri = $"csharp://task/{taskId}/build-log/root-causes";
        var lines = new List<string>
        {
            $"Total issues: {buildTriage.TotalIssueCount}",
            $"Errors: {buildTriage.ErrorCount}",
            $"Warnings: {buildTriage.WarningCount}",
            $"Cascade issues: {buildTriage.CascadeIssueCount}",
        };
        lines.AddRange(buildTriage.Issues.Select(issue =>
            $"{issue.Id} {issue.Kind} root={issue.IsRootCauseCandidate} cascade={issue.IsLikelyCascade} file={issue.Span?.FilePath ?? issue.ProjectName}: {issue.Message}"));

        _evidenceStore.AddTextResource(
            uri,
            "build-log-root-causes",
            "Build log root-cause candidates",
            "Compact build-log triage issues for an Agentic C# build failure session.",
            "text/plain",
            string.Join(Environment.NewLine, lines),
            false);

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = "build-log",
            Title = "Build log root-cause candidates",
            Summary = $"{buildTriage.TotalIssueCount} build issue(s)",
            IsPartial = false,
        };
    }

    private EvidenceResourceLink CreateDiagnosticsResourceLink(string taskId, CodeDiagnostic[] diagnostics, string name)
    {
        var uri = $"csharp://task/{taskId}/diagnostics";
        var lines = diagnostics.Select(diagnostic =>
            $"{diagnostic.Id} {diagnostic.Severity} {diagnostic.ProjectName} {diagnostic.Span?.FilePath}: {diagnostic.Message}");

        _evidenceStore.AddTextResource(
            uri,
            name,
            "Scoped diagnostics",
            "Compact scoped Roslyn diagnostics for an Agentic C# workflow task.",
            "text/plain",
            string.Join(Environment.NewLine, lines),
            false);

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = "diagnostics",
            Title = "Scoped diagnostics",
            Summary = $"{diagnostics.Length} diagnostic(s)",
            IsPartial = false,
        };
    }

    private EvidenceResourceLink CreateContextResourceLink(string taskId, CSharpTaskContextPackage context)
    {
        var uri = $"csharp://task/{taskId}/context";
        var text = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {context.Status}",
                $"Problem: {context.TaskContext.ProblemText}",
                $"File: {context.TaskContext.FilePath}",
                $"Symbol: {context.TaskContext.SymbolQuery}",
                $"Project: {context.TaskContext.ProjectName}",
                $"Primary files: {context.PrimaryFiles.Length}",
                $"Primary symbols: {context.PrimarySymbols.Length}",
                $"Primary diagnostics: {context.PrimaryDiagnostics.Length}",
                $"Source snippets: {context.SourceSnippets.Length}",
            });

        _evidenceStore.AddTextResource(
            uri,
            "task-context",
            "Task context summary",
            "Compact Agentic C# workflow task context.",
            "text/plain",
            text,
            false);

        return new EvidenceResourceLink
        {
            Uri = uri,
            Kind = "context",
            Title = "Task context summary",
            Summary = context.Status,
            IsPartial = false,
        };
    }

    private EvidenceResourceLink CreateContextJsonResourceLink(string taskId, CSharpTaskContextPackage context)
    {
        return CreateJsonResourceLink(
            $"csharp://task/{taskId}/context.json",
            "task-context-json",
            "Task context JSON",
            "Machine-readable Agentic C# workflow task context.",
            "context-json",
            context.Status,
            new
            {
                context.Status,
                context.TaskContext,
                context.EvidenceLevel,
                context.PrimaryFiles,
                context.PrimarySymbols,
                context.PrimaryDiagnostics,
                context.ImpactSummary,
                context.RelatedTests,
                SourceSnippetCount = context.SourceSnippets.Length,
                context.RecommendedNextActions,
                context.SuggestedNextSteps,
            },
            false);
    }

    private EvidenceResourceLink[] CreateSourceResourceLinks(string taskId, SourceContextSnippet[] snippets)
    {
        return snippets.Select((snippet, index) =>
        {
            var title = snippet.Symbol is null ? snippet.FilePath : FormatSymbolName(snippet.Symbol);
            var uri = $"csharp://task/{taskId}/source/{index + 1}";
            _evidenceStore.AddTextResource(
                uri,
                $"source-{index + 1}",
                title,
                CreateSourceResourceDescription(snippet),
                "text/plain",
                snippet.Text,
                snippet.IsTextTruncated);

            return new EvidenceResourceLink
            {
                Uri = uri,
                Kind = "source",
                Title = title,
                Summary = snippet.FilePath,
                IsPartial = snippet.IsTextTruncated,
            };
        }).ToArray();
    }

    private static string CreateSourceResourceDescription(SourceContextSnippet snippet)
    {
        var span = snippet.FocusSpan;
        return $"{snippet.ContextKind} {snippet.FilePath}:{span.StartLine}";
    }

    private static EvidenceItem CreateFindingEvidenceItem(string id, AreaAuditFinding finding)
    {
        return new EvidenceItem
        {
            Id = id,
            Kind = AgentWorkflowEvidenceKind.Inference,
            EvidenceLevel = finding.EvidenceLevel,
            Summary = string.IsNullOrWhiteSpace(finding.Summary) ? finding.Title : $"{finding.Title}: {finding.Summary}",
            SourceRef = finding.FilePath,
            Span = finding.Span,
            ProjectName = finding.ProjectName,
            RelevanceReasons = finding.Reasons,
            Confidence = finding.Confidence,
            IsPartial = false,
        };
    }

    private static EvidenceItem CreateCommandEvidenceItem(string id, VerificationCommand command)
    {
        return new EvidenceItem
        {
            Id = id,
            Kind = AgentWorkflowEvidenceKind.Heuristic,
            EvidenceLevel = WorkflowEvidenceLevel.Heuristic,
            Summary = $"{command.Scope}: {command.Command}",
            SourceRef = command.Command,
            RelevanceReasons = command.Reasons,
            Confidence = command.Confidence,
            IsPartial = false,
        };
    }

    private static EvidenceItem CreateDebugFrameEvidenceItem(string id, DebugStackFrameInfo frame)
    {
        return new EvidenceItem
        {
            Id = id,
            Kind = AgentWorkflowEvidenceKind.DebugFrame,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = frame.FunctionName,
            SourceRef = frame.Span?.FilePath ?? frame.ModuleName,
            Span = frame.Span,
            RelevanceReasons = frame.IsCurrent ? new[] { "current debug frame" } : new[] { "debug call stack" },
            Confidence = "high",
            IsPartial = false,
        };
    }

    private static EvidenceItem CreateBuildIssueEvidenceItem(string id, BuildIssue issue)
    {
        return new EvidenceItem
        {
            Id = id,
            Kind = AgentWorkflowEvidenceKind.BuildLog,
            EvidenceLevel = WorkflowEvidenceLevel.Fact,
            Summary = $"{issue.Id}: {issue.Message}",
            SourceRef = issue.Span?.FilePath ?? issue.ProjectName,
            Span = issue.Span,
            ProjectName = issue.ProjectName,
            RelevanceReasons = issue.RankReasons,
            Confidence = issue.IsRootCauseCandidate ? "high" : issue.IsLikelyCascade ? "low" : "medium",
            IsPartial = false,
        };
    }

    private static string FormatOptionalSymbolName(SymbolDescriptor? symbol)
    {
        return symbol is null ? "None" : FormatSymbolName(symbol);
    }

    private static string[] CreateReviewResidualRisks(CSharpChangeReviewReport review, bool isPartial)
    {
        var risks = new List<string>();
        if (isPartial)
        {
            risks.Add("当前审查证据存在 partial 或截断，扩大修改范围前需要补充上下文。");
        }

        if (review.RelatedTests.Length == 0)
        {
            risks.Add("没有发现直接相关测试证据，验证计划需要至少覆盖受影响项目。");
        }

        if (review.PublicApiRisks.Length > 0)
        {
            risks.Add("存在公共 API 或跨项目影响风险，应用修改前应先确认兼容边界。");
        }

        return risks.ToArray();
    }

    private static string[] CreateVerificationResidualRisks(CSharpRegressionScopePlan plan, bool isPartial)
    {
        var risks = new List<string>();
        if (isPartial)
        {
            risks.Add("当前验证计划存在 partial 或截断，执行广泛修改前应补充范围证据。");
        }

        if (plan.FocusedCommands.Length == 0)
        {
            risks.Add("没有形成 focused test 命令，可能只能用项目或 solution build 兜底。");
        }

        if (plan.BroadCommands.Length > 0)
        {
            risks.Add("存在 broad 验证命令，通常应在 smoke/focused 通过后再执行。");
        }

        return risks.ToArray();
    }

    private static string[] CreateRuntimeResidualRisks(DebugSessionPreparationPlan plan, ArtifactEvidenceReport? artifactEvidence, bool isPartial)
    {
        var risks = new List<string>();
        if (isPartial)
        {
            risks.Add("当前 runtime exception 证据存在 partial 或截断，需要重新收窄或增加上限后再下结论。");
        }

        if (plan.DebuggerStatus?.IsPaused != true)
        {
            risks.Add("调试器未暂停，当前没有可靠的现场调用栈和局部变量证据。");
        }

        if (artifactEvidence is null || artifactEvidence.Artifacts.Length == 0)
        {
            risks.Add("没有附加 report/trace/log artifact 证据，异常根因判断主要依赖调试器状态。");
        }

        return risks.ToArray();
    }

    private static string[] CreateBuildFailureResidualRisks(CSharpBuildFailureContext context, BuildFailureSession session, bool isPartial)
    {
        var risks = new List<string>();
        if (isPartial)
        {
            risks.Add("当前 build failure session 存在 partial 或截断，应补充完整 build log 或收窄范围后再下结论。");
        }

        if (session.RootCauseCandidates.Length == 0)
        {
            risks.Add("没有明确 root-cause candidate，后续应先补充 build output 或 scoped diagnostics。");
        }

        if (context.BuildOutputWindow?.IsTruncated == true)
        {
            risks.Add("Visual Studio Build Output 被截断，显式 build log 比 Output Window 尾部更可靠。");
        }

        return risks.ToArray();
    }

    private static string FormatSymbolName(SymbolDescriptor symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol.ContainingType))
        {
            return $"{symbol.ContainingType}.{symbol.Name}";
        }

        if (!string.IsNullOrWhiteSpace(symbol.ContainingNamespace))
        {
            return $"{symbol.ContainingNamespace}.{symbol.Name}";
        }

        return symbol.Name;
    }
}
