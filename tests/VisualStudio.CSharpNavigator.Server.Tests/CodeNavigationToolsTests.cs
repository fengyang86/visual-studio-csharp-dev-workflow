using VisualStudio.CSharpNavigator.Abstractions;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;
using VisualStudio.CSharpNavigator.Server.Tools;
using ModelContextProtocol.Server;
using System.Reflection;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class CodeNavigationToolsTests
{
    [Theory]
    [InlineData(nameof(CodeNavigationTools.FindCSharpDefinitions))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpReferences))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpImplementations))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpOverrides))]
    [InlineData(nameof(CodeNavigationTools.DescribeCSharpSymbol))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpCallers))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpCallees))]
    [InlineData(nameof(CodeNavigationTools.AnalyzeCSharpSymbolImpact))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpRelatedTests))]
    [InlineData(nameof(CodeNavigationTools.PreviewCSharpRename))]
    [InlineData(nameof(CodeNavigationTools.ApplyCSharpRename))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpDerivedTypes))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpInheritanceChain))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpSymbolSource))]
    public void SymbolLookupTools_ExposePositionParametersAsOptionalForMcpClients(string methodName)
    {
        var method = typeof(CodeNavigationTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Could not find method {methodName}.");
        var parameters = method.GetParameters().ToDictionary(parameter => parameter.Name!);

        AssertOptionalNull(parameters["symbolKey"]);
        AssertOptionalNull(parameters["filePath"]);
        AssertOptionalNull(parameters["line"]);
        AssertOptionalNull(parameters["column"]);
    }

    [Theory]
    [InlineData(nameof(CodeNavigationTools.GetCSharpWorkspaceStatus))]
    [InlineData(nameof(CodeNavigationTools.StartCSharpInvestigation))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpTaskContext))]
    [InlineData(nameof(CodeNavigationTools.PlanCSharpVerification))]
    [InlineData(nameof(CodeNavigationTools.AuditCSharpArea))]
    [InlineData(nameof(CodeNavigationTools.ReviewCSharpChange))]
    [InlineData(nameof(CodeNavigationTools.CheckVisualStudioCSharpNavigatorHealth))]
    [InlineData(nameof(CodeNavigationTools.SearchCSharpSymbols))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpDefinitions))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpReferences))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpReferencesBySymbolSearch))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpImplementations))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpOverrides))]
    [InlineData(nameof(CodeNavigationTools.DescribeCSharpSymbol))]
    [InlineData(nameof(CodeNavigationTools.ListCSharpDocumentSymbols))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpDiagnostics))]
    [InlineData(nameof(CodeNavigationTools.GetVisualStudioErrorList))]
    [InlineData(nameof(CodeNavigationTools.GetVisualStudioOutputWindow))]
    [InlineData(nameof(CodeNavigationTools.GetVisualStudioOpenDocuments))]
    [InlineData(nameof(CodeNavigationTools.GetVisualStudioActiveDocumentContext))]
    [InlineData(nameof(CodeNavigationTools.OpenCSharpSourceLocation))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpCallers))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpCallees))]
    [InlineData(nameof(CodeNavigationTools.AnalyzeCSharpSymbolImpact))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpRelatedTests))]
    [InlineData(nameof(CodeNavigationTools.PreviewCSharpRename))]
    [InlineData(nameof(CodeNavigationTools.ApplyCSharpRename))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpDerivedTypes))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpInheritanceChain))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpProjectGraph))]
    [InlineData(nameof(CodeNavigationTools.GetDebuggerStatus))]
    [InlineData(nameof(CodeNavigationTools.GetDebugCallStack))]
    [InlineData(nameof(CodeNavigationTools.GetDebugStackFrameVariables))]
    [InlineData(nameof(CodeNavigationTools.EvaluateDebugExpression))]
    [InlineData(nameof(CodeNavigationTools.ListDebugThreads))]
    [InlineData(nameof(CodeNavigationTools.ListDebugBreakpoints))]
    [InlineData(nameof(CodeNavigationTools.StartDebugging))]
    [InlineData(nameof(CodeNavigationTools.ContinueDebugging))]
    [InlineData(nameof(CodeNavigationTools.BreakDebugging))]
    [InlineData(nameof(CodeNavigationTools.StopDebugging))]
    [InlineData(nameof(CodeNavigationTools.StepOver))]
    [InlineData(nameof(CodeNavigationTools.StepInto))]
    [InlineData(nameof(CodeNavigationTools.StepOut))]
    [InlineData(nameof(CodeNavigationTools.SetDebugBreakpoint))]
    [InlineData(nameof(CodeNavigationTools.RemoveDebugBreakpoint))]
    [InlineData(nameof(CodeNavigationTools.EnableDebugBreakpoint))]
    [InlineData(nameof(CodeNavigationTools.ListCSharpGeneratedDocuments))]
    [InlineData(nameof(CodeNavigationTools.FindCSharpTemporaryMarkers))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpEnclosingContext))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpSymbolSource))]
    [InlineData(nameof(CodeNavigationTools.BatchGetCSharpSymbolSources))]
    [InlineData(nameof(CodeNavigationTools.GetCSharpSourceContext))]
    [InlineData(nameof(CodeNavigationTools.BatchGetCSharpSourceContexts))]
    public void TargetedTools_ExposeTargetParametersAsOptionalForMcpClients(string methodName)
    {
        var method = typeof(CodeNavigationTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Could not find method {methodName}.");
        var parameters = method.GetParameters().ToDictionary(parameter => parameter.Name!);

        AssertOptionalNull(parameters["targetPipeName"]);
        AssertOptionalNull(parameters["targetInstanceId"]);
        AssertOptionalNull(parameters["targetSolutionPath"]);
    }

    [Fact]
    public void EvaluateDebugExpression_IsNotMarkedReadOnlyBecauseSideEffectsCanBeExplicitlyAllowed()
    {
        var method = typeof(DebugContextTools).GetMethod(nameof(DebugContextTools.EvaluateDebugExpression))
            ?? throw new InvalidOperationException("Could not find EvaluateDebugExpression.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException("EvaluateDebugExpression is missing McpServerToolAttribute.");

        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Idempotent);
    }

    [Fact]
    public void ApplyCSharpRename_IsNotMarkedReadOnlyBecauseItAppliesWorkspaceChanges()
    {
        var method = typeof(RefactoringTools).GetMethod(nameof(RefactoringTools.ApplyCSharpRename))
            ?? throw new InvalidOperationException("Could not find ApplyCSharpRename.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException("ApplyCSharpRename is missing McpServerToolAttribute.");

        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Idempotent);
    }

    [Fact]
    public void OpenCSharpSolutionInVisualStudio_IsNotMarkedReadOnlyBecauseItLaunchesVisualStudio()
    {
        var method = typeof(WorkspacePreparationTools).GetMethod(nameof(WorkspacePreparationTools.OpenCSharpSolutionInVisualStudio))
            ?? throw new InvalidOperationException("Could not find OpenCSharpSolutionInVisualStudio.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException("OpenCSharpSolutionInVisualStudio is missing McpServerToolAttribute.");

        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Idempotent);
    }

    [Fact]
    public void OpenCSharpSourceLocation_IsNotMarkedReadOnlyBecauseItChangesVisualStudioFocus()
    {
        var method = typeof(VisualStudioDocumentTools).GetMethod(nameof(VisualStudioDocumentTools.OpenCSharpSourceLocation))
            ?? throw new InvalidOperationException("Could not find OpenCSharpSourceLocation.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException("OpenCSharpSourceLocation is missing McpServerToolAttribute.");

        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Idempotent);
    }

    [Theory]
    [InlineData(nameof(WorkflowEnhancementTools.InvestigateCSharpBuildFailure), "investigate_csharp_build_failure")]
    [InlineData(nameof(WorkflowEnhancementTools.CollectArtifactEvidence), "collect_artifact_evidence")]
    [InlineData(nameof(WorkflowEnhancementTools.WaitForArtifactEvidence), "wait_for_artifact_evidence")]
    [InlineData(nameof(WorkflowEnhancementTools.PlanCSharpRegressionScope), "plan_csharp_regression_scope")]
    [InlineData(nameof(WorkflowEnhancementTools.PrepareDebugSession), "prepare_debug_session")]
    [InlineData(nameof(WorkflowEnhancementTools.PlanCSharpDebugScenario), "plan_csharp_debug_scenario")]
    [InlineData(nameof(WorkflowEnhancementTools.AnalyzeCSharpRepoWorkflow), "analyze_csharp_repo_workflow")]
    [InlineData(nameof(WorkflowEnhancementTools.GenerateCSharpAgentInstructions), "generate_csharp_agent_instructions")]
    [InlineData(nameof(WorkflowEnhancementTools.SplitCSharpAgentWork), "split_csharp_agent_work")]
    [InlineData(nameof(WorkflowEnhancementTools.MergeCSharpAgentFindings), "merge_csharp_agent_findings")]
    [InlineData(nameof(WorkflowEnhancementTools.GetCSharpWorkflowPerformanceSnapshot), "get_csharp_workflow_performance_snapshot")]
    public void WorkflowEnhancementTools_AreReadOnlyAndExposeExpectedToolNames(string methodName, string toolName)
    {
        var method = typeof(WorkflowEnhancementTools).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Could not find {methodName}.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException($"{methodName} is missing McpServerToolAttribute.");

        Assert.True(attribute.ReadOnly);
        Assert.True(attribute.Idempotent);
        Assert.Equal(toolName, attribute.Name);
    }

    [Fact]
    public void PrepareCSharpEditTask_IsReadOnlyAndIdempotent()
    {
        var method = typeof(AgenticWorkflowTools).GetMethod(nameof(AgenticWorkflowTools.PrepareCSharpEditTask))
            ?? throw new InvalidOperationException("Could not find PrepareCSharpEditTask.");
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException("PrepareCSharpEditTask is missing McpServerToolAttribute.");

        Assert.True(attribute.ReadOnly);
        Assert.True(attribute.Idempotent);
        Assert.Equal("prepare_csharp_edit_task", attribute.Name);
    }

    [Fact]
    public async Task SearchCSharpSymbols_WhenTargetIsProvided_ForwardsTarget()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.SearchCSharpSymbols(
            "SetProps",
            targetPipeName: "pipe-a",
            targetInstanceId: "instance-a",
            targetSolutionPath: @"D:\WorkCodes\A\A.sln",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastSearchRequest);
        Assert.Equal("pipe-a", bridge.LastSearchRequest.Target?.PipeName);
        Assert.Equal("instance-a", bridge.LastSearchRequest.Target?.InstanceId);
        Assert.Equal(@"D:\WorkCodes\A\A.sln", bridge.LastSearchRequest.Target?.SolutionPath);
    }

    [Fact]
    public async Task SearchCSharpSymbols_WhenRepeated_UsesShortLivedCache()
    {
        var bridge = new RecordingBridge
        {
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Name = "Widget",
                    ProjectName = "SampleWorkspace.Core",
                    Kind = CodeSymbolKind.Type,
                }),
        };
        var tools = new CodeNavigationTools(bridge, new ShortLivedQueryCache(TimeSpan.FromMinutes(1)));

        var first = await tools.SearchCSharpSymbols("Widget", cancellationToken: CancellationToken.None);
        var second = await tools.SearchCSharpSymbols("Widget", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Contains("ServerCacheHit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Contains("ServerCacheHit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchCSharpSymbols_WhenRequestDiffers_DoesNotReuseCache()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge, new ShortLivedQueryCache(TimeSpan.FromMinutes(1)));

        await tools.SearchCSharpSymbols("Widget", maxResults: 10, cancellationToken: CancellationToken.None);
        await tools.SearchCSharpSymbols("Widget", maxResults: 11, cancellationToken: CancellationToken.None);

        Assert.Equal(2, bridge.CallCount);
    }

    [Fact]
    public async Task GetCSharpWorkspaceStatus_WhenTargetIsNotProvided_ForwardsNullTarget()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetCSharpWorkspaceStatus(cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastWorkspaceStatusRequest);
        Assert.Null(bridge.LastWorkspaceStatusRequest.Target);
    }

    [Fact]
    public async Task FindCSharpSolutions_WhenSlnAndSlnxExist_RanksPreferredCandidateAndSkipsNoise()
    {
        using var temp = TemporaryDirectory.Create();
        var rootSolution = Path.Combine(temp.Path, "RootSolution.sln");
        var nestedDirectory = Path.Combine(temp.Path, "src", "Feature");
        var artifactsDirectory = Path.Combine(temp.Path, "artifacts");
        Directory.CreateDirectory(nestedDirectory);
        Directory.CreateDirectory(artifactsDirectory);
        File.WriteAllText(rootSolution, string.Empty);
        File.WriteAllText(Path.Combine(nestedDirectory, "Feature.slnx"), string.Empty);
        File.WriteAllText(Path.Combine(artifactsDirectory, "Ignored.sln"), string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.FindCSharpSolutions(
            temp.Path,
            preferredName: "Feature",
            maxDepth: 4,
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsPartial);
        Assert.DoesNotContain(result.Items, item => item.FileName == "Ignored.sln");
        Assert.Equal(2, result.Items.Count);
        var first = result.Items[0];
        Assert.Equal("Feature.slnx", first.FileName);
        Assert.True(first.IsSlnx);
        Assert.Contains(first.Reasons, reason => reason.Contains("preferred", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine("src", "Feature", "Feature.slnx"), first.RelativePath);
    }

    [Fact]
    public async Task FindCSharpSolutions_WhenSlnxDisabled_ReturnsOnlySlnFiles()
    {
        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(temp.Path, "B.slnx"), string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.FindCSharpSolutions(
            temp.Path,
            includeSlnx: false,
            cancellationToken: CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("A.sln", item.FileName);
    }

    [Fact]
    public async Task FindCSharpSolutions_WhenMaxResultsTruncates_ReturnsPartialDiagnostic()
    {
        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(temp.Path, "B.sln"), string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.FindCSharpSolutions(
            temp.Path,
            maxResults: 1,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Single(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("SolutionCandidatesTruncated:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindCSharpSolutions_WhenRootIsMissing_ReturnsFailure()
    {
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.FindCSharpSolutions(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing"),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Solution search root does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareCSharpWorkspace_WhenMatchingBridgeExists_ReturnsReadyTarget()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(new VisualStudioBridgeInstanceDescriptor
            {
                InstanceId = "instance-a",
                PipeName = "pipe-a",
                SolutionPath = solutionPath,
                IsAlive = true,
            }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PrepareCSharpWorkspace(
            temp.Path,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.True(report.IsReady);
        Assert.NotNull(report.SelectedSolution);
        Assert.NotNull(report.SelectedInstance);
        Assert.Equal("instance-a", report.SelectedInstance.InstanceId);
        Assert.False(report.RequiresVisualStudioLaunch);
    }

    [Fact]
    public async Task PrepareCSharpWorkspace_WhenNoBridgeExists_ReturnsLaunchRequired()
    {
        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "RootSolution.sln"), string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.PrepareCSharpWorkspace(
            temp.Path,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("VisualStudioLaunchRequired", report.Status);
        Assert.True(report.RequiresVisualStudioLaunch);
        Assert.NotNull(report.SelectedSolution);
        Assert.Contains(report.SuggestedNextSteps, step => step.Contains("Open Visual Studio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareCSharpWorkspace_WhenCandidatesTie_ReturnsAmbiguousSolution()
    {
        using var temp = TemporaryDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "A.sln"), string.Empty);
        File.WriteAllText(Path.Combine(temp.Path, "B.sln"), string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.PrepareCSharpWorkspace(
            temp.Path,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("AmbiguousSolution", report.Status);
        Assert.True(report.IsAmbiguous);
        Assert.Null(report.SelectedSolution);
        Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task OpenCSharpSolutionInVisualStudio_WhenMatchingBridgeExists_DoesNotLaunch()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(new VisualStudioBridgeInstanceDescriptor
            {
                InstanceId = "instance-a",
                PipeName = "pipe-a",
                SolutionPath = solutionPath,
                IsAlive = true,
            }),
        };
        var launcher = new RecordingSolutionLauncher();
        var tools = new CodeNavigationTools(bridge, solutionLauncher: launcher);

        var result = await tools.OpenCSharpSolutionInVisualStudio(
            solutionPath,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("AlreadyOpen", report.Status);
        Assert.False(report.Started);
        Assert.NotNull(report.SelectedInstance);
        Assert.Equal("instance-a", report.SelectedInstance.InstanceId);
        Assert.Equal(0, launcher.LaunchCount);
    }

    [Fact]
    public async Task OpenCSharpSolutionInVisualStudio_WhenNoBridge_LaunchesAndWaitsForBridge()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var bridge = new RecordingBridge
        {
            InstancesResults = new Queue<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>>(new[]
            {
                QueryResult<VisualStudioBridgeInstanceDescriptor>(),
                QueryResult(new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = solutionPath,
                    IsAlive = true,
                }),
            }),
        };
        var launcher = new RecordingSolutionLauncher
        {
            DevenvPath = @"C:\VS\devenv.exe",
            ProcessId = 42,
        };
        var tools = new CodeNavigationTools(bridge, solutionLauncher: launcher);

        var result = await tools.OpenCSharpSolutionInVisualStudio(
            solutionPath,
            timeoutMilliseconds: 1000,
            pollIntervalMilliseconds: 100,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.True(report.Started);
        Assert.Equal(42, report.ProcessId);
        Assert.Equal(1, launcher.LaunchCount);
        Assert.NotNull(report.SelectedInstance);
        Assert.Equal("instance-a", report.SelectedInstance.InstanceId);
        Assert.NotNull(report.WaitReport);
        Assert.True(report.WaitReport.IsReady);
    }

    [Fact]
    public async Task OpenCSharpSolutionInVisualStudio_WhenBridgeDoesNotAppear_ReturnsTimedOutLaunchReport()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var launcher = new RecordingSolutionLauncher
        {
            DevenvPath = @"C:\VS\devenv.exe",
            ProcessId = 42,
        };
        var tools = new CodeNavigationTools(
            new RecordingBridge(),
            solutionLauncher: launcher,
            activityLogReader: new RecordingActivityLogReader());

        var result = await tools.OpenCSharpSolutionInVisualStudio(
            solutionPath,
            timeoutMilliseconds: 0,
            pollIntervalMilliseconds: 100,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.True(result.IsPartial);
        Assert.Equal("LaunchedBridgeTimedOut", report.Status);
        Assert.True(report.Started);
        Assert.Equal(42, report.ProcessId);
        Assert.NotNull(report.WaitReport);
        Assert.Equal("TimedOut", report.WaitReport.Status);
        Assert.False(report.WaitReport.IsReady);
    }

    [Fact]
    public async Task OpenCSharpSolutionInVisualStudio_WhenBridgeDoesNotAppear_AddsLaunchAndActivityLogDiagnostics()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var launcher = new RecordingSolutionLauncher
        {
            DevenvPath = @"C:\VS\devenv.exe",
            ProcessId = 42,
            Diagnostics = new[] { @"VisualStudioLaunchEnvironment: windir=C:\WINDOWS; useShellExecute=False." },
        };
        var activityLogReader = new RecordingActivityLogReader(
            "VisualStudioActivityLogIssue: Error | Microsoft.VisualStudio.Shell.ViewManager | SetSite failed for package [WindowManagementPackage] | System.UriFormatException");
        var tools = new CodeNavigationTools(
            new RecordingBridge(),
            solutionLauncher: launcher,
            activityLogReader: activityLogReader);

        var result = await tools.OpenCSharpSolutionInVisualStudio(
            solutionPath,
            timeoutMilliseconds: 0,
            pollIntervalMilliseconds: 100,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("LaunchedBridgeTimedOut", report.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("VisualStudioLaunchEnvironment", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("WindowManagementPackage", StringComparison.Ordinal));
        Assert.Equal(1, activityLogReader.ReadCount);
    }

    [Fact]
    public async Task WaitForVisualStudioBridge_WhenTimedOut_ReturnsPartialReport()
    {
        using var temp = TemporaryDirectory.Create();
        var solutionPath = Path.Combine(temp.Path, "RootSolution.sln");
        File.WriteAllText(solutionPath, string.Empty);
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.WaitForVisualStudioBridge(
            solutionPath,
            timeoutMilliseconds: 0,
            pollIntervalMilliseconds: 100,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.True(result.IsPartial);
        Assert.Equal("TimedOut", report.Status);
        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task CheckVisualStudioCSharpNavigatorHealth_WhenMultipleInstancesAndNoTarget_DoesNotGuess()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                },
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-b",
                    PipeName = "pipe-b",
                    SolutionPath = @"D:\B\B.sln",
                    IsAlive = true,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.CheckVisualStudioCSharpNavigatorHealth(cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Null(bridge.LastWorkspaceStatusRequest);
        var report = Assert.Single(result.Items);
        Assert.Equal("AmbiguousTarget", report.Status);
        Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task CheckVisualStudioCSharpNavigatorHealth_WhenSingleInstanceAndDiagnosticsPreview_ForwardsScopedDiagnostics()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.CheckVisualStudioCSharpNavigatorHealth(
            includeDiagnosticsPreview: true,
            includePathPatterns: new[] { @"src\Feature" },
            excludePathPatterns: new[] { @"src\ACADPlugins" },
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            projectName: "Feature.Project",
            noiseProfile: CodeDiagnosticNoiseProfile.Filter,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, bridge.CallCount);
        Assert.NotNull(bridge.LastWorkspaceStatusRequest);
        Assert.Equal("instance-a", bridge.LastWorkspaceStatusRequest.Target?.InstanceId);
        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Equal(new[] { @"src\Feature" }, bridge.LastDiagnosticsRequest.IncludePathPatterns);
        Assert.Equal(new[] { @"src\ACADPlugins" }, bridge.LastDiagnosticsRequest.ExcludePathPatterns);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, bridge.LastDiagnosticsRequest.ChangedFiles);
        Assert.Equal("Feature.Project", bridge.LastDiagnosticsRequest.ProjectName);
        Assert.Equal(CodeDiagnosticNoiseProfile.Filter, bridge.LastDiagnosticsRequest.NoiseProfile);
        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
    }

    [Fact]
    public async Task CheckVisualStudioCSharpNavigatorHealth_WhenBridgeProtocolDiffers_ReturnsVersionMismatch()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    BridgeProtocolVersion = "999",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.CheckVisualStudioCSharpNavigatorHealth(cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("VersionMismatch", report.Status);
        Assert.True(result.IsPartial);
        Assert.Equal(VisualStudioBridgeInstanceDescriptor.ExpectedBridgeProtocolVersion, report.ExpectedBridgeProtocolVersion);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("BridgeProtocolMismatch"));
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenMultipleInstancesAndNoTarget_DoesNotGuess()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                },
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-b",
                    PipeName = "pipe-b",
                    SolutionPath = @"D:\B\B.sln",
                    IsAlive = true,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            buildOutput: @"D:\A\Feature.cs(10,5): error CS0103: The name 'x' does not exist in the current context [D:\A\A.csproj]",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Null(bridge.LastWorkspaceStatusRequest);
        var report = Assert.Single(result.Items);
        Assert.Equal("AmbiguousTarget", report.Status);
        Assert.NotNull(report.BuildTriage);
        Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenUniqueSymbolExists_GathersScopedContext()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            buildOutput: @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
            symbolQuery: "Save",
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            excludePathPatterns: new[] { @"src\ACADPlugins" },
            noiseProfile: CodeDiagnosticNoiseProfile.Filter,
            maxRelatedItems: 7,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastWorkspaceStatusRequest);
        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Contains(@"src\Feature\Widget.cs", bridge.LastDiagnosticsRequest.IncludePathPatterns);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, bridge.LastDiagnosticsRequest.ChangedFiles);
        Assert.Equal(new[] { @"src\ACADPlugins" }, bridge.LastDiagnosticsRequest.ExcludePathPatterns);
        Assert.Equal(CodeDiagnosticNoiseProfile.Filter, bridge.LastDiagnosticsRequest.NoiseProfile);
        Assert.NotNull(bridge.LastSearchRequest);
        Assert.Equal("Save", bridge.LastSearchRequest.QueryText);
        Assert.NotNull(bridge.LastDefinitionRequest);
        Assert.NotNull(bridge.LastReferenceRequest);
        Assert.NotNull(bridge.LastCallersRequest);
        Assert.NotNull(bridge.LastCalleesRequest);
        Assert.NotNull(bridge.LastRelatedTestsRequest);
        Assert.Equal(7, bridge.LastReferenceRequest.MaxResults);
        Assert.Equal(7, bridge.LastCallersRequest.MaxResults);
        Assert.Equal(7, bridge.LastCalleesRequest.MaxResults);
        Assert.Equal(7, bridge.LastRelatedTestsRequest.MaxResults);
        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.NotNull(report.BuildTriage);
        Assert.Single(report.Symbols);
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenBuildAndChangedFilesExist_ReturnsPrimaryWorkflowContext()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            DiagnosticsResult = QueryResult(
                new CodeDiagnostic
                {
                    Id = "CS0103",
                    Severity = CodeDiagnosticSeverity.Error,
                    Message = "The name 'state' does not exist in the current context",
                    ProjectName = "Feature.Project",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 10,
                        StartColumn = 5,
                        EndLine = 10,
                        EndColumn = 10,
                    },
                    RelevanceScore = 100,
                    ScopeReasons = new[] { "changed file" },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            problemText: "Fix build error after widget change",
            buildOutput: @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Equal("Fix build error after widget change", report.TaskContext.ProblemText);
        Assert.Equal("explicit-build-input", report.TaskContext.BuildEvidenceSource);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, report.TaskContext.ChangedFiles);
        Assert.Contains(report.PrimaryFiles, file =>
            file.FilePath.EndsWith(@"Widget.cs", StringComparison.OrdinalIgnoreCase)
            && file.Reasons.Any(reason => reason.Contains("changed file", StringComparison.OrdinalIgnoreCase))
            && file.Reasons.Any(reason => reason.Contains("build issue", StringComparison.OrdinalIgnoreCase)));
        var primaryDiagnostic = Assert.Single(report.PrimaryDiagnostics, diagnostic => diagnostic.Source == "build");
        Assert.Equal("CS0103", primaryDiagnostic.Id);
        Assert.False(primaryDiagnostic.IsLikelyCascade);
        Assert.Contains(report.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.InspectDiagnostic
            && action.SuggestedTool == "get_csharp_enclosing_context"
            && action.TargetFilePath.EndsWith(@"Widget.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenSymbolAmbiguous_ReturnsNarrowScopeAction()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                },
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Other.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Other.Project",
                    Kind = CodeSymbolKind.Method,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            symbolQuery: "Save",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Null(bridge.LastReferenceRequest);
        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Equal(2, report.PrimarySymbols.Length);
        Assert.Contains(report.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.NarrowScope
            && action.SuggestedTool == "search_csharp_symbols");
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenBuildOutputIsOmitted_UsesVisualStudioBuildOutput()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            OutputWindowResult = QueryResult(
                new VisualStudioOutputWindowSnapshot
                {
                    PaneName = "Build",
                    Text = @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
                    TotalCharacters = 140,
                    ReturnedCharacters = 140,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastOutputWindowRequest);
        Assert.Equal("Build", bridge.LastOutputWindowRequest.PaneName);
        Assert.Equal(20000, bridge.LastOutputWindowRequest.MaxCharacters);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("VisualStudioBuildOutputUsed", StringComparison.Ordinal));
        var report = Assert.Single(result.Items);
        Assert.NotNull(report.BuildTriage);
        Assert.Contains(report.BuildTriage.Issues, issue => issue.Id == "CS0103");
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenBuildOutputIsProvided_DoesNotReadVisualStudioBuildOutput()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            OutputWindowResult = QueryResult(
                new VisualStudioOutputWindowSnapshot
                {
                    PaneName = "Build",
                    Text = @"D:\A\src\Feature\Other.cs(3,1): error CS0246: The type or namespace name 'Missing' could not be found [D:\A\Feature.Project.csproj]",
                    TotalCharacters = 130,
                    ReturnedCharacters = 130,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            buildOutput: @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Null(bridge.LastOutputWindowRequest);
        var report = Assert.Single(result.Items);
        Assert.NotNull(report.BuildTriage);
        Assert.Contains(report.BuildTriage.Issues, issue => issue.Id == "CS0103");
        Assert.DoesNotContain(report.BuildTriage.Issues, issue => issue.Id == "CS0246");
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenMaxRelatedItemsIsZero_SkipsRelatedContext()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            symbolQuery: "Save",
            maxRelatedItems: 0,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastDefinitionRequest);
        Assert.Null(bridge.LastReferenceRequest);
        Assert.Null(bridge.LastCallersRequest);
        Assert.Null(bridge.LastCalleesRequest);
        Assert.Null(bridge.LastRelatedTestsRequest);
        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Single(report.Symbols);
        Assert.Empty(report.References);
        Assert.Empty(report.Callers);
        Assert.Empty(report.Callees);
        Assert.Empty(report.RelatedTests);
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenUniqueSymbolExists_ReturnsImpactSummary()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                }),
            ImpactResult = QueryResult(
                new SymbolImpactSummary
                {
                    Symbol = new SymbolDescriptor
                    {
                        Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                        Name = "Save",
                        ContainingType = "Widget",
                        ProjectName = "Feature.Project",
                        Kind = CodeSymbolKind.Method,
                    },
                    TotalReferences = 4,
                    DistinctFileCount = 3,
                    DistinctProjectCount = 2,
                    HasCrossProjectImpact = true,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            symbolQuery: "Save",
            maxRelatedItems: 5,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastImpactRequest);
        Assert.Equal(2, bridge.LastImpactRequest.MaxDepth);
        Assert.Equal(5, bridge.LastImpactRequest.MaxResults);
        var report = Assert.Single(result.Items);
        Assert.NotNull(report.ImpactSummary);
        Assert.True(report.ImpactSummary.HasCrossProjectImpact);
        Assert.Contains(report.RecommendedNextActions, action =>
            action.SuggestedTool == "analyze_csharp_symbol_impact"
            && action.Confidence == "high");
    }

    [Fact]
    public async Task StartCSharpInvestigation_WhenSymbolContextIsPartial_MarksInvestigationPartial()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                }),
            ReferenceResult = PartialQueryResult<SymbolReference>("References truncated at MaxResults=1."),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.StartCSharpInvestigation(
            symbolQuery: "Save",
            maxRelatedItems: 1,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("References truncated"));
    }

    [Fact]
    public async Task GetCSharpTaskContext_WhenPrimaryEvidenceExists_ReturnsBoundedSourceSnippets()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                }),
            SymbolSourceResult = QueryResult(
                new SourceContextSnippet
                {
                    FilePath = @"D:\A\src\Feature\Widget.cs",
                    ContextKind = "method",
                    FocusSpan = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                    Text = "void Save() { }",
                }),
            SourceContextResult = QueryResult(
                new SourceContextSnippet
                {
                    FilePath = @"D:\A\src\Feature\Widget.cs",
                    ContextKind = "method",
                    FocusSpan = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 10,
                        StartColumn = 5,
                        EndLine = 10,
                        EndColumn = 10,
                    },
                    Text = "void Broken() { }",
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetCSharpTaskContext(
            problemText: "Fix widget build failure",
            buildOutput: @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
            symbolQuery: "Save",
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            maxSourceSnippets: 2,
            contextLines: 2,
            maxCharsPerSnippet: 3000,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Equal("Ready", package.Status);
        Assert.Equal("Fix widget build failure", package.TaskContext.ProblemText);
        Assert.NotNull(package.BuildTriage);
        Assert.NotEmpty(package.PrimaryFiles);
        Assert.NotEmpty(package.PrimarySymbols);
        Assert.Equal(2, package.SourceSnippets.Length);
        Assert.Contains(package.SourceSnippets, snippet => snippet.Text.Contains("Save", StringComparison.Ordinal));
        Assert.Contains(package.SourceSnippets, snippet => snippet.Text.Contains("Broken", StringComparison.Ordinal));
        Assert.NotNull(bridge.LastSymbolSourceRequest);
        Assert.NotNull(bridge.LastSourceContextRequest);
        Assert.Equal("docid:M:Sample.Feature.Widget.Save", bridge.LastSymbolSourceRequest.SymbolKey?.Value);
        Assert.Equal(2, bridge.LastSourceContextRequest.ContextLines);
        Assert.Equal(3000, bridge.LastSourceContextRequest.MaxChars);
    }

    [Fact]
    public async Task GetCSharpTaskContext_WhenSnippetLimitIsZero_SkipsSourceRequests()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetCSharpTaskContext(
            buildOutput: @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
            maxSourceSnippets: 0,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Empty(package.SourceSnippets);
        Assert.Null(bridge.LastSymbolSourceRequest);
        Assert.Null(bridge.LastSourceContextRequest);
        Assert.Contains(package.SuggestedNextSteps, step => step.Contains("Source snippets were disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareCSharpEditTask_WhenNoBridge_ReturnsEvidencePacketAndSafetyBlocker()
    {
        var tools = new AgenticWorkflowTools(CreateWorkflowKernel(new RecordingBridge()));

        var result = await tools.PrepareCSharpEditTask(
            problemText: "Fix missing state field",
            symbolQuery: "Save",
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Equal("NoActiveBridge", package.Status);
        Assert.Equal(AgentWorkflowTaskKind.EditTask, package.EvidencePacket.TaskKind);
        Assert.Contains(package.EvidencePacket.SafetyBlockers, blocker => blocker.Code == "NoActiveBridge");
        Assert.Contains(package.SuggestedNextSteps, step => step.Contains("Open Visual Studio", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.IsPartial);
    }

    [Fact]
    public void TaskRouter_WhenEditTaskHasBuildEvidence_RoutesToBuildFailureWorkflow()
    {
        var router = new TaskRouter();
        var request = new CSharpEditTaskRequest
        {
            ProblemText = "Fix compile error",
            BuildOutput = @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist",
            FilePath = @"D:\A\src\Feature\Widget.cs",
        };
        var context = new CSharpTaskContextPackage
        {
            Status = "Ready",
            PrimaryDiagnostics = new[]
            {
                new PrimaryDiagnostic
                {
                    Source = "build-output",
                    Id = "CS0103",
                    Message = "The name 'state' does not exist",
                    ProjectName = "Feature.Project",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 10,
                        StartColumn = 5,
                        EndLine = 10,
                        EndColumn = 10,
                    },
                },
            },
        };

        var actions = router.RouteEditTask(request, context);

        Assert.Contains(actions, action =>
            action.Kind == WorkflowActionKind.InspectDiagnostic
            && action.SuggestedTool == "investigate_csharp_build_failure"
            && action.TargetProjectName == "Feature.Project");
    }

    [Fact]
    public void TaskRouter_WhenVerificationPlanHasCommand_RoutesToShellCommand()
    {
        var router = new TaskRouter();
        var plan = new CSharpRegressionScopePlan
        {
            VerificationPlan = new CSharpVerificationPlan
            {
                Status = "Ready",
            },
            FocusedCommands = new[]
            {
                new VerificationCommand
                {
                    Command = "dotnet test .\\tests\\Feature.Project.Tests\\Feature.Project.Tests.csproj -c Release --no-build",
                    Scope = "Feature.Project.Tests",
                    Confidence = "high",
                },
            },
        };

        var actions = router.RouteVerificationRun(new CSharpVerificationRunTaskRequest(), plan);

        Assert.Contains(actions, action =>
            action.Kind == WorkflowActionKind.RunTests
            && action.SuggestedTool == "shell"
            && action.SuggestedCommand.Contains("dotnet test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareCSharpEditTask_WhenPrimaryEvidenceExists_ReturnsEditCandidates()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                }),
            SymbolSourceResult = QueryResult(
                new SourceContextSnippet
                {
                    FilePath = @"D:\A\src\Feature\Widget.cs",
                    ContextKind = "method",
                    Symbol = new SymbolDescriptor
                    {
                        Name = "Save",
                        ContainingType = "Widget",
                        ProjectName = "Feature.Project",
                    },
                    FocusSpan = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                    Text = "void Save() { }",
                }),
        };
        var evidenceStore = new EvidenceStore();
        var tools = new AgenticWorkflowTools(CreateWorkflowKernel(bridge, evidenceStore));

        var result = await tools.PrepareCSharpEditTask(
            problemText: "Fix widget save",
            symbolQuery: "Save",
            targetInstanceId: "instance-a",
            maxSourceSnippets: 1,
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Equal("Ready", package.Status);
        Assert.NotEmpty(package.EditCandidates);
        Assert.NotEmpty(package.EvidencePacket.PrimaryFindings);
        Assert.NotEmpty(package.EvidencePacket.ResourceLinks);
        Assert.NotEmpty(evidenceStore.ListResources());
        Assert.Equal("prepare_csharp_edit_task", package.EvidencePacket.Telemetry.WorkflowName);
        Assert.Null(package.TaskContextPackage);
        Assert.NotNull(package.WorkspaceContextLease);
        Assert.Equal("pipe-a", package.WorkspaceContextLease!.Target.PipeName);
    }

    [Fact]
    public async Task PrepareCSharpChangeReview_WhenScopeAndSymbolAreProvided_ReturnsEvidencePacket()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            DiagnosticsResult = QueryResult(
                new CodeDiagnostic
                {
                    Id = "CS8602",
                    Message = "Dereference of a possibly null reference",
                    Severity = CodeDiagnosticSeverity.Warning,
                    ProjectName = "Feature.Project",
                    RelevanceScore = 90,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 18,
                        StartColumn = 9,
                        EndLine = 18,
                        EndColumn = 14,
                    },
                    ScopeReasons = new[] { "changed file" },
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                }),
            ImpactResult = QueryResult(
                new SymbolImpactSummary
                {
                    Symbol = new SymbolDescriptor
                    {
                        Key = new SymbolKey("docid:M:Feature.Widget.Save"),
                        Name = "Save",
                        ContainingType = "Widget",
                        ProjectName = "Feature.Project",
                        Kind = CodeSymbolKind.Method,
                    },
                    TotalReferences = 8,
                    DistinctFileCount = 5,
                    DistinctProjectCount = 2,
                    HasCrossProjectImpact = true,
                }),
            TemporaryMarkersResult = QueryResult(
                new TemporaryMarker
                {
                    ProjectName = "Feature.Project",
                    Marker = "TODO",
                    Text = "TODO: remove review shortcut",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 22,
                        StartColumn = 9,
                        EndLine = 22,
                        EndColumn = 37,
                    },
                }),
        };
        var evidenceStore = new EvidenceStore();
        var tools = new AgenticWorkflowTools(CreateWorkflowKernel(bridge, evidenceStore));

        var result = await tools.PrepareCSharpChangeReview(
            problemText: "Review widget save change",
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            symbolQuery: "Save",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Equal("Ready", package.Status);
        Assert.NotEmpty(package.PublicApiRisks);
        Assert.NotEmpty(package.TestGaps);
        Assert.Contains(package.EvidencePacket.ResourceLinks, link => link.Kind == "review");
        Assert.Contains(package.EvidencePacket.ResourceLinks, link => link.Kind == "impact-graph");
        Assert.Contains(package.EvidencePacket.ResourceLinks, link => link.Kind == "diagnostics");
        Assert.NotEmpty(evidenceStore.ListResources());
        Assert.Equal("prepare_csharp_change_review", package.EvidencePacket.Telemetry.WorkflowName);
        Assert.Equal(AgentWorkflowTaskKind.ChangeReview, package.EvidencePacket.TaskKind);
    }

    [Fact]
    public async Task PrepareCSharpVerificationRun_WhenChangedFilesExist_ReturnsEvidencePacket()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(
                new ProjectGraph
                {
                    Nodes = new[]
                    {
                        new ProjectGraphNode
                        {
                            ProjectName = "Feature.Project",
                            FilePath = @"D:\A\src\Feature\Feature.Project.csproj",
                            DocumentCount = 10,
                        },
                        new ProjectGraphNode
                        {
                            ProjectName = "Feature.Project.Tests",
                            FilePath = @"D:\A\tests\Feature.Project.Tests\Feature.Project.Tests.csproj",
                            DocumentCount = 5,
                        },
                    },
                }),
            RelatedTestsResult = QueryResult(
                new RelatedTestDescriptor
                {
                    ProjectName = "Feature.Project.Tests",
                    TestClass = "WidgetTests",
                    TestMethod = "Save_persists_state",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\tests\Feature.Project.Tests\WidgetTests.cs",
                        StartLine = 12,
                        StartColumn = 17,
                        EndLine = 12,
                        EndColumn = 36,
                    },
                    MatchReasons = new[] { "ReferenceMatch" },
                }),
        };
        var evidenceStore = new EvidenceStore();
        var tools = new AgenticWorkflowTools(CreateWorkflowKernel(bridge, evidenceStore));

        var result = await tools.PrepareCSharpVerificationRun(
            problemText: "Verify widget save change",
            changedFiles: new[] { @"D:\A\src\Feature\Widget.cs" },
            symbolQuery: "Save",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Equal("Ready", package.Status);
        Assert.NotEmpty(package.FocusedCommands);
        Assert.Contains(package.EvidencePacket.ResourceLinks, link => link.Kind == "verification");
        Assert.NotEmpty(evidenceStore.ListResources());
        Assert.Equal("prepare_csharp_verification_run", package.EvidencePacket.Telemetry.WorkflowName);
        Assert.Equal(AgentWorkflowTaskKind.Verification, package.EvidencePacket.TaskKind);
    }

    [Fact]
    public async Task InvestigateCSharpRuntimeException_WhenNoDebugSession_ReturnsEvidencePacketAndSafetyBlocker()
    {
        var evidenceStore = new EvidenceStore();
        var tools = new AgenticWorkflowTools(CreateWorkflowKernel(new RecordingBridge(), evidenceStore));

        var result = await tools.InvestigateCSharpRuntimeException(
            problemText: "Investigate crash on save",
            exceptionText: "System.InvalidOperationException: Save failed",
            maxSourceSnippets: 0,
            cancellationToken: CancellationToken.None);

        var package = Assert.Single(result.Items);
        Assert.Contains(package.EvidencePacket.SafetyBlockers, blocker => blocker.Code == "NoActiveDebugSession");
        Assert.Contains(package.EvidencePacket.ResourceLinks, link => link.Kind == "debug-evidence");
        Assert.NotEmpty(evidenceStore.ListResources());
        Assert.Equal("investigate_csharp_runtime_exception", package.EvidencePacket.Telemetry.WorkflowName);
        Assert.Equal(AgentWorkflowTaskKind.RuntimeDebug, package.EvidencePacket.TaskKind);
    }

    [Fact]
    public async Task PlanCSharpDebugScenario_WhenBreakpointAndArtifactsProvided_ReturnsExplicitSteps()
    {
        var bridge = new RecordingBridge();
        var tools = new WorkflowEnhancementTools(new CodeNavigationTools(bridge));

        var result = await tools.PlanCSharpDebugScenario(
            problemText: "Reproduce crash on save",
            scenarioName: "save-crash",
            breakpointFilePath: @"D:\A\src\Feature\Widget.cs",
            breakpointLine: 42,
            artifactPaths: new[] { @"D:\A\logs\save.log" },
            stopDebuggingAtEnd: true,
            maxSourceSnippets: 0,
            cancellationToken: CancellationToken.None);

        var plan = Assert.Single(result.Items);
        Assert.Equal("save-crash", plan.ScenarioName);
        Assert.Contains(plan.Steps, step =>
            step.ToolName == "set_debug_breakpoint"
            && step.IsMutating
            && step.RequiresExplicitTarget);
        Assert.Contains(plan.Steps, step => step.ToolName == "start_debugging" && step.IsMutating);
        Assert.Contains(plan.Steps, step => step.ToolName == "prepare_debug_session" && !step.IsMutating);
        Assert.Contains(plan.Steps, step => step.ToolName == "collect_artifact_evidence" && !step.IsMutating);
        Assert.Contains(plan.Steps, step => step.ToolName == "stop_debugging" && step.IsMutating);
        Assert.Contains(plan.RecommendedNextActions, action => action.SuggestedTool == "start_debugging");
    }

    [Fact]
    public void EvidenceStore_WhenTextResourceAdded_ListsAndReadsResource()
    {
        var store = new EvidenceStore();

        store.AddTextResource(
            "csharp://task/test/source/1",
            "source-1",
            "Widget.Save",
            "method D:\\A\\Widget.cs:20",
            "text/plain",
            "void Save() { }",
            isPartial: false);

        var resource = Assert.Single(store.ListResources());
        Assert.Equal("csharp://task/test/source/1", resource.Uri);
        Assert.Equal("source-1", resource.Name);
        Assert.Equal("Widget.Save", resource.Title);

        var read = store.ReadResource("csharp://task/test/source/1");
        var contents = Assert.Single(read.Contents);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(contents);
        Assert.Equal("void Save() { }", text.Text);
        Assert.Equal("text/plain", text.MimeType);
    }

    [Fact]
    public void EvidenceStore_WhenJsonResourceAdded_ListsAndReadsResource()
    {
        var store = new EvidenceStore();

        store.AddJsonResource(
            "csharp://task/test/context.json",
            "task-context-json",
            "Task context JSON",
            "machine-readable context",
            new { status = "Ready", primaryFiles = 1 },
            isPartial: false);

        var resource = Assert.Single(store.ListResources());
        Assert.Equal("application/json", resource.MimeType);

        var read = store.ReadResource("csharp://task/test/context.json");
        var contents = Assert.Single(read.Contents);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(contents);
        Assert.Equal("application/json", text.MimeType);
        Assert.Contains("\"status\"", text.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ready", text.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvidenceResourceHandlers_ReturnResourcesAndTemplates()
    {
        var store = new EvidenceStore();
        store.AddTextResource(
            "csharp://task/test/source/1",
            "source-1",
            "Widget.Save",
            "method D:\\A\\Widget.cs:20",
            "text/plain",
            "void Save() { }",
            isPartial: false);
        var handlers = new EvidenceResourceHandlers(store);

        Assert.Single(handlers.ListResources().Resources);
        Assert.Single(handlers.ReadResource("csharp://task/test/source/1").Contents);
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/source/{index}");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/context.json");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/review");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/review.json");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/verification");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/verification.json");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/debug-evidence");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/debug-evidence.json");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/build-failure");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/build-failure.json");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/build-log/{issueId}");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/diagnostics");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/impact-graph");
        Assert.Contains(handlers.ListResourceTemplates().ResourceTemplates, template => template.UriTemplate == "csharp://task/{taskId}/artifact/{artifactId}");
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenChangedFilesAndRelatedTestsExist_ReturnsFocusedCommands()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(
                new ProjectGraph
                {
                    Nodes = new[]
                    {
                        new ProjectGraphNode
                        {
                            ProjectId = "feature",
                            ProjectName = "Feature.Project",
                            FilePath = @"D:\A\src\Feature\Feature.Project.csproj",
                            Language = "C#",
                        },
                        new ProjectGraphNode
                        {
                            ProjectId = "feature-tests",
                            ProjectName = "Feature.Project.Tests",
                            FilePath = @"D:\A\tests\Feature.Project.Tests\Feature.Project.Tests.csproj",
                            Language = "C#",
                        },
                    },
                }),
            RelatedTestsResult = QueryResult(
                new RelatedTestDescriptor
                {
                    ProjectName = "Feature.Project.Tests",
                    TestClass = "Feature.Project.Tests.WidgetTests",
                    TestMethod = "SavePersistsState",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\tests\Feature.Project.Tests\WidgetTests.cs",
                        StartLine = 12,
                        StartColumn = 5,
                        EndLine = 12,
                        EndColumn = 22,
                    },
                    MatchReasons = new[] { "ReferenceMatch" },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            noiseProfile: CodeDiagnosticNoiseProfile.Off,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastProjectGraphRequest);
        Assert.Equal("instance-a", bridge.LastProjectGraphRequest.Target?.InstanceId);
        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, bridge.LastDiagnosticsRequest.ChangedFiles);
        Assert.Equal(CodeDiagnosticNoiseProfile.Off, bridge.LastDiagnosticsRequest.NoiseProfile);
        Assert.NotNull(bridge.LastRelatedTestsRequest);
        Assert.Equal(@"D:\A\src\Feature\Widget.cs", bridge.LastRelatedTestsRequest.FilePath);
        var plan = Assert.Single(result.Items);
        Assert.Equal("Ready", plan.Status);
        Assert.Equal(WorkflowEvidenceLevel.Fact, plan.EvidenceLevel);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, plan.TaskContext.ChangedFiles);
        Assert.Contains(plan.AffectedProjects, project => project.ProjectName == "Feature.Project");
        Assert.Contains(plan.AffectedProjects, project => project.ProjectName == "Feature.Project.Tests" && project.IsTestProject);
        Assert.Contains(plan.RecommendedCommands, command =>
            command.Scope == "project-build"
            && command.Command.Contains(@"D:\A\src\Feature\Feature.Project.csproj", StringComparison.Ordinal));
        Assert.Contains(plan.RecommendedCommands, command =>
            command.Scope == "single-test"
            && command.Command.Contains("FullyQualifiedName~Feature.Project.Tests.WidgetTests.SavePersistsState", StringComparison.Ordinal));
        Assert.Contains(plan.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.RunTests
            && action.SuggestedCommand.Contains("FullyQualifiedName~Feature.Project.Tests.WidgetTests.SavePersistsState", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenRelatedTestsAreHeuristicOnly_DoesNotRecommendSingleTestCommands()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(
                new ProjectGraph
                {
                    Nodes = new[]
                    {
                        new ProjectGraphNode
                        {
                            ProjectId = "feature-tests",
                            ProjectName = "Feature.Project.Tests",
                            FilePath = @"D:\A\tests\Feature.Project.Tests\Feature.Project.Tests.csproj",
                            Language = "C#",
                        },
                    },
                }),
            RelatedTestsResult = QueryResult(
                new RelatedTestDescriptor
                {
                    ProjectName = "Feature.Project.Tests",
                    TestClass = "Feature.Project.Tests.WidgetTests",
                    TestMethod = "SavePersistsState",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\tests\Feature.Project.Tests\WidgetTests.cs",
                        StartLine = 12,
                        StartColumn = 5,
                        EndLine = 12,
                        EndColumn = 22,
                    },
                    MatchReasons = new[] { "NameMatch:Widget" },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var plan = Assert.Single(result.Items);
        Assert.DoesNotContain(plan.RecommendedCommands, command => command.Scope == "single-test");
        Assert.Contains(plan.RecommendedCommands, command =>
            command.Scope == "test-project"
            && command.Confidence == "low"
            && command.Reasons.Contains("heuristic test evidence only"));
        Assert.Contains(plan.RelatedTests, test => test.MatchReasons.Contains("NameMatch:Widget"));
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenBuildOutputIsOmitted_UsesVisualStudioBuildOutput()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(new ProjectGraph()),
            OutputWindowResult = QueryResult(
                new VisualStudioOutputWindowSnapshot
                {
                    PaneName = "Build",
                    Text = @"D:\A\src\Feature\Widget.cs(10,5): error CS0103: The name 'state' does not exist in the current context [D:\A\Feature.Project.csproj]",
                    TotalCharacters = 140,
                    ReturnedCharacters = 140,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastOutputWindowRequest);
        var plan = Assert.Single(result.Items);
        Assert.NotNull(plan.BuildTriage);
        Assert.Contains(plan.BuildTriage.Issues, issue => issue.Id == "CS0103");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("VisualStudioBuildOutputUsed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenRelatedTestsArePartial_MarksPlanPartial()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(new ProjectGraph()),
            RelatedTestsResult = PartialQueryResult<RelatedTestDescriptor>("Related tests truncated at MaxResults=1."),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            maxRelatedTests: 1,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Related tests truncated"));
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenDiagnosticsArePartial_RecommendsNarrowDiagnostics()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            ProjectGraphResult = QueryResult(new ProjectGraph()),
            DiagnosticsResult = PartialQueryResult<CodeDiagnostic>("DiagnosticsTimeBudgetExceeded: processed 1 of 2 matching C# project(s)."),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        var plan = Assert.Single(result.Items);
        Assert.Contains(plan.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.NarrowScope
            && action.SuggestedTool == "get_csharp_diagnostics");
    }

    [Fact]
    public async Task PlanCSharpVerification_WhenNoActiveBridge_ReturnsMachineReadableNextAction()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PlanCSharpVerification(
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            targetSolutionPath: @"D:\A\A.sln",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        var plan = Assert.Single(result.Items);
        Assert.Equal("NoActiveBridge", plan.Status);
        Assert.Contains(plan.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.OpenVisualStudio
            && action.Confidence == "high"
            && action.SuggestedCommand.Contains(@"D:\A\A.sln", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuditCSharpArea_WhenScopeAndQueryAreProvided_ReturnsCompactEvidence()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            DiagnosticsResult = QueryResult(
                new CodeDiagnostic
                {
                    Id = "CS0168",
                    Message = "The variable 'state' is declared but never used",
                    Severity = CodeDiagnosticSeverity.Warning,
                    ProjectName = "Feature.Project",
                    RelevanceScore = 120,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 10,
                        StartColumn = 9,
                        EndLine = 10,
                        EndColumn = 14,
                    },
                    ScopeReasons = new[] { "changed file" },
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:T:Feature.Widget"),
                    Name = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Type,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 5,
                        StartColumn = 14,
                        EndLine = 5,
                        EndColumn = 20,
                    },
                }),
            RelatedTestsResult = QueryResult(
                new RelatedTestDescriptor
                {
                    ProjectName = "Feature.Project.Tests",
                    TestClass = "Feature.Project.Tests.WidgetTests",
                    TestMethod = "SavesState",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\tests\Feature.Project.Tests\WidgetTests.cs",
                        StartLine = 12,
                        StartColumn = 5,
                        EndLine = 12,
                        EndColumn = 15,
                    },
                    MatchReasons = new[] { "file name heuristic" },
                }),
            TemporaryMarkersResult = QueryResult(
                new TemporaryMarker
                {
                    ProjectName = "Feature.Project",
                    Marker = "TODO",
                    Text = "TODO: replace fixture shortcut",
                    EnclosingSymbol = "Feature.Widget.Save",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 15,
                        StartColumn = 12,
                        EndLine = 15,
                        EndColumn = 42,
                    },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.AuditCSharpArea(
            problemText: "Audit widget save behavior",
            areaPaths: new[] { @"src\Feature" },
            queryTerms: new[] { "Widget" },
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            maxDiagnosticProjects: 2,
            maxDiagnosticElapsedMilliseconds: 12000,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Contains(@"src\Feature", bridge.LastDiagnosticsRequest.IncludePathPatterns);
        Assert.Contains(@"src\Feature\Widget.cs", bridge.LastDiagnosticsRequest.ChangedFiles);
        Assert.Equal(2, bridge.LastDiagnosticsRequest.MaxProjects);
        Assert.Equal(12000, bridge.LastDiagnosticsRequest.MaxElapsedMilliseconds);
        Assert.NotNull(bridge.LastSearchRequest);
        Assert.Equal("Widget", bridge.LastSearchRequest.QueryText);
        Assert.NotNull(bridge.LastRelatedTestsRequest);
        Assert.Equal(@"D:\A\src\Feature\Widget.cs", bridge.LastRelatedTestsRequest.FilePath);
        Assert.NotNull(bridge.LastTemporaryMarkersRequest);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Equal(WorkflowEvidenceLevel.Fact, report.EvidenceLevel);
        Assert.Contains(report.Findings, finding => finding.Category == "diagnostic" && finding.Title == "CS0168");
        Assert.Contains(report.Findings, finding => finding.Category == "symbol-candidate" && finding.Title == "Widget");
        Assert.Contains(report.Findings, finding => finding.Category == "related-test" && finding.Title.Contains("SavesState", StringComparison.Ordinal));
        Assert.Contains(report.CoveredBehaviors, finding => finding.Title.Contains("SavesState", StringComparison.Ordinal));
        Assert.Contains(report.CoverageGaps, finding => finding.Title.Contains("Temporary marker", StringComparison.Ordinal));
        Assert.Empty(report.SuggestedTests);
        Assert.Contains(report.CandidateEditLocations, finding => finding.Title.Contains("Temporary marker", StringComparison.Ordinal));
        Assert.Contains(report.RecommendedNextActions, action => action.Kind == WorkflowActionKind.InspectDiagnostic);
    }

    [Fact]
    public async Task AuditCSharpArea_WhenNoRelatedTestsSuggestsFocusedTestsAndEditLocations()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:T:Feature.Widget"),
                    Name = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Type,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 5,
                        StartColumn = 14,
                        EndLine = 5,
                        EndColumn = 20,
                    },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.AuditCSharpArea(
            problemText: "Audit widget save behavior",
            areaPaths: new[] { @"src\Feature" },
            queryTerms: new[] { "Widget" },
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Empty(report.CoveredBehaviors);
        Assert.Contains(report.CoverageGaps, finding => finding.Title == "No related test evidence");
        Assert.Contains(report.SuggestedTests, finding => finding.Category == "suggested-test" && finding.Title.Contains("Widget", StringComparison.Ordinal));
        Assert.Contains(report.CandidateEditLocations, finding => finding.Category == "candidate-edit-location" && finding.Title == "Widget");
    }

    [Fact]
    public async Task ReviewCSharpChange_WhenScopeAndSymbolAreProvided_ReturnsRisksAndGaps()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
            DiagnosticsResult = QueryResult(
                new CodeDiagnostic
                {
                    Id = "CS8602",
                    Message = "Dereference of a possibly null reference",
                    Severity = CodeDiagnosticSeverity.Warning,
                    ProjectName = "Feature.Project",
                    RelevanceScore = 90,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 18,
                        StartColumn = 9,
                        EndLine = 18,
                        EndColumn = 14,
                    },
                    ScopeReasons = new[] { "changed file" },
                }),
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Feature.Widget.Save"),
                    Name = "Save",
                    ContainingType = "Widget",
                    ProjectName = "Feature.Project",
                    Kind = CodeSymbolKind.Method,
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 20,
                        StartColumn = 17,
                        EndLine = 20,
                        EndColumn = 21,
                    },
                }),
            ImpactResult = QueryResult(
                new SymbolImpactSummary
                {
                    Symbol = new SymbolDescriptor
                    {
                        Key = new SymbolKey("docid:M:Feature.Widget.Save"),
                        Name = "Save",
                        ContainingType = "Widget",
                        ProjectName = "Feature.Project",
                        Kind = CodeSymbolKind.Method,
                    },
                    TotalReferences = 8,
                    DistinctFileCount = 5,
                    DistinctProjectCount = 2,
                    HasCrossProjectImpact = true,
                }),
            TemporaryMarkersResult = QueryResult(
                new TemporaryMarker
                {
                    ProjectName = "Feature.Project",
                    Marker = "TODO",
                    Text = "TODO: remove review shortcut",
                    Span = new SourceSpan
                    {
                        FilePath = @"D:\A\src\Feature\Widget.cs",
                        StartLine = 22,
                        StartColumn = 9,
                        EndLine = 22,
                        EndColumn = 37,
                    },
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.ReviewCSharpChange(
            problemText: "Review widget save change",
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            symbolQuery: "Save",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(bridge.LastImpactRequest);
        Assert.NotNull(bridge.LastTemporaryMarkersRequest);
        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.NotNull(report.ImpactSummary);
        Assert.Contains(report.PublicApiRisks, finding => finding.Category == "impact-risk" && finding.Confidence == "high");
        Assert.Contains(report.TestGaps, finding => finding.Title == "No related test evidence");
        Assert.Contains(report.CandidateEditLocations, finding => finding.Title.Contains("Temporary marker", StringComparison.Ordinal));
        Assert.Contains(report.RecommendedNextActions, action => action.SuggestedTool == "analyze_csharp_symbol_impact");
    }

    [Fact]
    public async Task SearchCSharpSymbols_WhenQueryIsEmpty_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SearchCSharpSymbols(" ", cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Query text is required"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpDefinitions_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpDefinitions("docid:M:SampleWorkspace.Sample.SetProps", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastDefinitionRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastDefinitionRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastDefinitionRequest.Position);
    }

    [Fact]
    public async Task FindCSharpReferences_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpReferences(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxResults: 25,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastReferenceRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastReferenceRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastReferenceRequest.Position);
        Assert.Equal(25, bridge.LastReferenceRequest.MaxResults);
    }

    [Fact]
    public async Task GetCSharpSymbolSource_WhenTargetAndSymbolKeyProvided_ForwardsRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetCSharpSymbolSource(
            "docid:M:SampleWorkspace.Sample.SetProps",
            contextLines: 4,
            maxChars: 5000,
            maxSnippets: 2,
            includeGeneratedCode: true,
            targetPipeName: "pipe-a",
            targetInstanceId: "instance-a",
            targetSolutionPath: @"D:\WorkCodes\A\A.sln",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastSymbolSourceRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastSymbolSourceRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastSymbolSourceRequest.Position);
        Assert.Equal(4, bridge.LastSymbolSourceRequest.ContextLines);
        Assert.Equal(5000, bridge.LastSymbolSourceRequest.MaxChars);
        Assert.Equal(2, bridge.LastSymbolSourceRequest.MaxSnippets);
        Assert.True(bridge.LastSymbolSourceRequest.IncludeGeneratedCode);
        Assert.Equal("pipe-a", bridge.LastSymbolSourceRequest.Target?.PipeName);
        Assert.Equal("instance-a", bridge.LastSymbolSourceRequest.Target?.InstanceId);
        Assert.Equal(@"D:\WorkCodes\A\A.sln", bridge.LastSymbolSourceRequest.Target?.SolutionPath);
    }

    [Fact]
    public async Task GetCSharpSourceContext_WhenPositionProvided_ForwardsRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetCSharpSourceContext(
            @"D:\WorkCodes\A\src\Sample.cs",
            line: 10,
            column: 5,
            contextLines: 2,
            maxChars: 3000,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastSourceContextRequest);
        Assert.Null(bridge.LastSourceContextRequest.SymbolKey);
        Assert.NotNull(bridge.LastSourceContextRequest.Position);
        Assert.Equal(@"D:\WorkCodes\A\src\Sample.cs", bridge.LastSourceContextRequest.Position.FilePath);
        Assert.Equal(10, bridge.LastSourceContextRequest.Position.StartLine);
        Assert.Equal(5, bridge.LastSourceContextRequest.Position.StartColumn);
        Assert.Equal(2, bridge.LastSourceContextRequest.ContextLines);
        Assert.Equal(3000, bridge.LastSourceContextRequest.MaxChars);
        Assert.Equal(1, bridge.LastSourceContextRequest.MaxSnippets);
        Assert.Equal("instance-a", bridge.LastSourceContextRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task BatchGetCSharpSourceContexts_WhenPositionsProvided_CombinesBridgeResults()
    {
        var bridge = new RecordingBridge
        {
            SourceContextResults = new Queue<WorkspaceQueryResult<SourceContextSnippet>>(new[]
            {
                QueryResult(new SourceContextSnippet { FilePath = @"D:\A\One.cs", Text = "one" }),
                new WorkspaceQueryResult<SourceContextSnippet>
                {
                    Items = new[] { new SourceContextSnippet { FilePath = @"D:\A\Two.cs", Text = "two" } },
                    Diagnostics = new[] { "Context truncated." },
                    IsPartial = true,
                },
            }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.BatchGetCSharpSourceContexts(
            new[]
            {
                new SourcePositionRequest { FilePath = @"D:\A\One.cs", Line = 10, Column = 3 },
                new SourcePositionRequest { FilePath = @"D:\A\Two.cs", Line = 20, Column = 5 },
            },
            contextLines: 2,
            maxCharsPerPosition: 3000,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, bridge.CallCount);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains(@"D:\A\Two.cs:20:5", StringComparison.Ordinal));
        Assert.NotNull(bridge.LastSourceContextRequest);
        Assert.Equal(@"D:\A\Two.cs", bridge.LastSourceContextRequest.Position?.FilePath);
        Assert.Equal(2, bridge.LastSourceContextRequest.ContextLines);
        Assert.Equal(3000, bridge.LastSourceContextRequest.MaxChars);
        Assert.Equal("instance-a", bridge.LastSourceContextRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task BatchGetCSharpSourceContexts_WhenMaxPositionsTruncates_ReturnsPartialDiagnostic()
    {
        var bridge = new RecordingBridge
        {
            SourceContextResult = QueryResult(new SourceContextSnippet { FilePath = @"D:\A\One.cs", Text = "one" }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.BatchGetCSharpSourceContexts(
            new[]
            {
                new SourcePositionRequest { FilePath = @"D:\A\One.cs", Line = 10, Column = 3 },
                new SourcePositionRequest { FilePath = @"D:\A\Two.cs", Line = 20, Column = 5 },
            },
            maxPositions: 1,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Single(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("BatchSourceContextsTruncated:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchGetCSharpSourceContexts_WhenPositionIsInvalid_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.BatchGetCSharpSourceContexts(
            new[] { new SourcePositionRequest { FilePath = @"D:\A\One.cs", Line = 0, Column = 3 } },
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Positions[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatchGetCSharpSymbolSources_WhenSymbolsProvided_CombinesBridgeResults()
    {
        var bridge = new RecordingBridge
        {
            SymbolSourceResults = new Queue<WorkspaceQueryResult<SourceContextSnippet>>(new[]
            {
                QueryResult(new SourceContextSnippet { FilePath = @"D:\A\One.cs", Text = "one" }),
                new WorkspaceQueryResult<SourceContextSnippet>
                {
                    Items = new[] { new SourceContextSnippet { FilePath = @"D:\A\Two.cs", Text = "two" } },
                    Diagnostics = new[] { "Symbol source truncated." },
                    IsPartial = true,
                },
            }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.BatchGetCSharpSymbolSources(
            new[]
            {
                new SourcePositionRequest { SymbolKey = "docid:M:Sample.One.Save" },
                new SourcePositionRequest { FilePath = @"D:\A\Two.cs", Line = 20, Column = 5 },
            },
            contextLines: 2,
            maxCharsPerSymbol: 3000,
            maxSnippetsPerSymbol: 1,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, bridge.CallCount);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains(@"D:\A\Two.cs:20:5", StringComparison.Ordinal));
        Assert.NotNull(bridge.LastSymbolSourceRequest);
        Assert.Equal(@"D:\A\Two.cs", bridge.LastSymbolSourceRequest.Position?.FilePath);
        Assert.Equal(2, bridge.LastSymbolSourceRequest.ContextLines);
        Assert.Equal(3000, bridge.LastSymbolSourceRequest.MaxChars);
        Assert.Equal(1, bridge.LastSymbolSourceRequest.MaxSnippets);
        Assert.Equal("instance-a", bridge.LastSymbolSourceRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task BatchGetCSharpSymbolSources_WhenSymbolPositionIsInvalid_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.BatchGetCSharpSymbolSources(
            new[] { new SourcePositionRequest { FilePath = @"D:\A\One.cs", Line = 10 } },
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Symbols[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCSharpSymbolSource_WhenRequestLimitsAreInvalid_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetCSharpSymbolSource(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxChars: 0,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxChars", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCSharpSymbolSource_WhenPositionIsPartial_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetCSharpSymbolSource(
            filePath: @"D:\WorkCodes\A\src\Sample.cs",
            line: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("complete source position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCSharpDiagnostics_WhenPathPatternsAreProvided_ForwardsFilters()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetCSharpDiagnostics(
            includePathPatterns: new[] { @"src\SampleWorkspace.Core", @"tests\*.cs" },
            excludePathPatterns: new[] { @"src\ACADPlugins", @"TZData_src" },
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            noiseProfile: CodeDiagnosticNoiseProfile.Filter,
            maxProjects: 3,
            maxElapsedMilliseconds: 12000,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Equal(new[] { @"src\SampleWorkspace.Core", @"tests\*.cs" }, bridge.LastDiagnosticsRequest.IncludePathPatterns);
        Assert.Equal(new[] { @"src\ACADPlugins", @"TZData_src" }, bridge.LastDiagnosticsRequest.ExcludePathPatterns);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, bridge.LastDiagnosticsRequest.ChangedFiles);
        Assert.Equal(CodeDiagnosticNoiseProfile.Filter, bridge.LastDiagnosticsRequest.NoiseProfile);
        Assert.Equal(3, bridge.LastDiagnosticsRequest.MaxProjects);
        Assert.Equal(12000, bridge.LastDiagnosticsRequest.MaxElapsedMilliseconds);
    }

    [Fact]
    public async Task GetCSharpDiagnostics_WhenNoiseProfileIsOmitted_DefaultsToAuto()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetCSharpDiagnostics(cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastDiagnosticsRequest);
        Assert.Equal(CodeDiagnosticNoiseProfile.Auto, bridge.LastDiagnosticsRequest.NoiseProfile);
        Assert.Equal(0, bridge.LastDiagnosticsRequest.MaxProjects);
        Assert.Equal(45000, bridge.LastDiagnosticsRequest.MaxElapsedMilliseconds);
    }

    [Fact]
    public async Task GetCSharpDiagnostics_WhenProjectOrElapsedLimitIsInvalid_ReturnsFailure()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var projectLimitResult = await tools.GetCSharpDiagnostics(
            maxProjects: -1,
            cancellationToken: CancellationToken.None);
        var elapsedLimitResult = await tools.GetCSharpDiagnostics(
            maxElapsedMilliseconds: 56000,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.Contains(projectLimitResult.Diagnostics, diagnostic => diagnostic.Contains("MaxProjects"));
        Assert.Contains(elapsedLimitResult.Diagnostics, diagnostic => diagnostic.Contains("MaxElapsedMilliseconds"));
    }

    [Fact]
    public async Task GetVisualStudioErrorList_WhenCalled_ForwardsTargetAndLimit()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetVisualStudioErrorList(
            maxResults: 25,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastErrorListRequest);
        Assert.Equal("instance-a", bridge.LastErrorListRequest.Target?.InstanceId);
        Assert.Equal(25, bridge.LastErrorListRequest.MaxResults);
    }

    [Fact]
    public async Task GetVisualStudioOutputWindow_WhenCalled_ForwardsTargetPaneAndLimit()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.GetVisualStudioOutputWindow(
            paneName: "Build",
            maxCharacters: 4096,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastOutputWindowRequest);
        Assert.Equal("instance-a", bridge.LastOutputWindowRequest.Target?.InstanceId);
        Assert.Equal("Build", bridge.LastOutputWindowRequest.PaneName);
        Assert.Equal(4096, bridge.LastOutputWindowRequest.MaxCharacters);
    }

    [Fact]
    public async Task GetVisualStudioOpenDocuments_WhenCalled_ForwardsTargetLimitAndSelection()
    {
        var bridge = new RecordingBridge
        {
            OpenDocumentsResult = QueryResult(new VisualStudioDocumentSnapshot { FilePath = @"D:\A\One.cs", IsActive = true }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetVisualStudioOpenDocuments(
            maxResults: 25,
            includeSelection: false,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Single(result.Items);
        Assert.NotNull(bridge.LastOpenDocumentsRequest);
        Assert.Equal("instance-a", bridge.LastOpenDocumentsRequest.Target?.InstanceId);
        Assert.Equal(25, bridge.LastOpenDocumentsRequest.MaxResults);
        Assert.False(bridge.LastOpenDocumentsRequest.IncludeSelection);
    }

    [Fact]
    public async Task GetVisualStudioActiveDocumentContext_WhenCalled_ForwardsTargetAndSelection()
    {
        var bridge = new RecordingBridge
        {
            ActiveDocumentResult = QueryResult(new VisualStudioDocumentSnapshot { FilePath = @"D:\A\Active.cs", IsActive = true }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetVisualStudioActiveDocumentContext(
            includeSelection: true,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Single(result.Items);
        Assert.NotNull(bridge.LastActiveDocumentRequest);
        Assert.Equal("instance-a", bridge.LastActiveDocumentRequest.Target?.InstanceId);
        Assert.Equal(1, bridge.LastActiveDocumentRequest.MaxResults);
        Assert.True(bridge.LastActiveDocumentRequest.IncludeSelection);
    }

    [Fact]
    public async Task OpenCSharpSourceLocation_WhenTargetIsMissing_ReturnsFailureWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.OpenCSharpSourceLocation(
            @"D:\A\Active.cs",
            line: 12,
            column: 4,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("requires an explicit target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OpenCSharpSourceLocation_WhenTargetProvided_ForwardsNavigationRequest()
    {
        var bridge = new RecordingBridge
        {
            SourceNavigationResult = QueryResult(new SourceNavigationResult
            {
                FilePath = @"D:\A\Active.cs",
                Line = 12,
                Column = 4,
                Opened = true,
                Activated = true,
            }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.OpenCSharpSourceLocation(
            @"D:\A\Active.cs",
            line: 12,
            column: 4,
            activate: true,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Single(result.Items);
        Assert.NotNull(bridge.LastSourceNavigationRequest);
        Assert.Equal("instance-a", bridge.LastSourceNavigationRequest.Target?.InstanceId);
        Assert.Equal(@"D:\A\Active.cs", bridge.LastSourceNavigationRequest.FilePath);
        Assert.Equal(12, bridge.LastSourceNavigationRequest.Line);
        Assert.Equal(4, bridge.LastSourceNavigationRequest.Column);
        Assert.True(bridge.LastSourceNavigationRequest.Activate);
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_RanksChangedPrimaryErrorsBeforeCascadeErrors()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = string.Join(
            Environment.NewLine,
            @"D:\WorkCodes\Sample\src\Legacy\Old.cs(4,5): error CS0103: The name 'LegacyThing' does not exist in the current context [D:\WorkCodes\Sample\Legacy.csproj]",
            @"CSC : error CS0006: Metadata file 'D:\WorkCodes\Sample\bin\Debug\Missing.dll' could not be found [D:\WorkCodes\Sample\App.csproj]",
            @"D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17): error CS0246: The type or namespace name 'WidgetState' could not be found [D:\WorkCodes\Sample\Feature.csproj]");

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        var report = Assert.Single(result.Items);
        Assert.Equal(3, report.TotalIssueCount);
        Assert.Equal(3, report.ErrorCount);
        Assert.Equal(1, report.CascadeIssueCount);
        Assert.Equal("CS0246", report.Issues[0].Id);
        Assert.True(report.Issues[0].IsInChangedFile);
        Assert.False(report.Issues[0].IsLikelyCascade);
        Assert.Contains("changed file", report.Issues[0].RankReasons);
        Assert.Equal("CS0006", report.Issues.Last().Id);
        Assert.True(report.Issues.Last().IsLikelyCascade);
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenPathFiltersAreProvided_SuppressesUnrelatedNoise()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = string.Join(
            Environment.NewLine,
            @"D:\WorkCodes\Sample\src\ACADPlugins\Plugin.cs(8,12): error CS0103: The name 'acad' does not exist in the current context [D:\WorkCodes\Sample\ACADPlugins.csproj]",
            @"D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17): warning CS8602: Dereference of a possibly null reference. [D:\WorkCodes\Sample\Feature.csproj]");

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            excludePathPatterns: new[] { @"src\ACADPlugins" },
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Single(report.Issues);
        Assert.Equal("CS8602", report.Issues[0].Id);
        Assert.Equal(1, report.SuppressedIssueCount);
        Assert.Contains(report.SuggestedNextSteps, step => step.Contains("suppressed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenVisualStudioOutputHasProjectPrefixes_StripsPrefixes()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = string.Join(
            Environment.NewLine,
            @"1>D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17): error CS0246: The type or namespace name 'WidgetState' could not be found [D:\WorkCodes\Sample\Feature.csproj]",
            @"2>CSC : error CS0006: Metadata file 'D:\WorkCodes\Sample\bin\Debug\Missing.dll' could not be found [D:\WorkCodes\Sample\App.csproj]");

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal(2, report.TotalIssueCount);
        var primary = report.Issues[0];
        Assert.Equal("CS0246", primary.Id);
        Assert.Equal(@"D:\WorkCodes\Sample\src\Feature\Widget.cs", primary.Span?.FilePath);
        Assert.True(primary.IsInChangedFile);
        Assert.Equal("CS0006", report.Issues.Last().Id);
        Assert.Equal("CSC", report.Issues.Last().Span?.FilePath);
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenDiagnosticHasFullSourceRange_PreservesEndSpan()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = @"D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17,10,28): warning CA1822: Member 'Save' does not access instance data and can be marked as static [D:\WorkCodes\Sample\Feature.csproj]";

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("CA1822", issue.Id);
        Assert.Equal(BuildIssueKind.Analyzer, issue.Kind);
        Assert.Equal(CodeDiagnosticSeverity.Warning, issue.Severity);
        Assert.Equal(10, issue.Span?.StartLine);
        Assert.Equal(17, issue.Span?.StartColumn);
        Assert.Equal(10, issue.Span?.EndLine);
        Assert.Equal(28, issue.Span?.EndColumn);
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenDiagnosticHasNoCode_UsesSyntheticId()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = string.Join(
            Environment.NewLine,
            @"D:\WorkCodes\Sample\App\App.csproj : error : The project file could not be loaded. Root element is missing.",
            @"D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17): warning : Generated file is out of date [D:\WorkCodes\Sample\Feature.csproj]");

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal(2, report.TotalIssueCount);
        Assert.Contains(report.Issues, issue =>
            issue.Id == "BUILDERROR"
            && issue.Kind == BuildIssueKind.MsBuild
            && issue.Severity == CodeDiagnosticSeverity.Error
            && issue.Span?.FilePath == @"D:\WorkCodes\Sample\App\App.csproj");
        Assert.Contains(report.Issues, issue =>
            issue.Id == "BUILDWARNING"
            && issue.Kind == BuildIssueKind.MsBuild
            && issue.Severity == CodeDiagnosticSeverity.Warning
            && issue.Span?.FilePath == @"D:\WorkCodes\Sample\src\Feature\Widget.cs");
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_ClassifiesRestoreTargetFrameworkAndGeneratedOutputIssues()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var buildOutput = string.Join(
            Environment.NewLine,
            @"D:\WorkCodes\Sample\App\Feature.cs(42,13): error CS0103: The name 'missingValue' does not exist in the current context [D:\WorkCodes\Sample\App\App.csproj]",
            @"D:\WorkCodes\Sample\App\App.csproj : error NU1101: Unable to find package Missing.Package. No packages exist with this id in source(s): nuget.org",
            @"D:\WorkCodes\Sample\App\App.csproj : error NU1202: Package Legacy.Widget 1.0.0 is not compatible with net8.0 (.NETCoreApp,Version=v8.0). Package Legacy.Widget 1.0.0 supports: net48 (.NETFramework,Version=v4.8)",
            @"D:\WorkCodes\Sample\App\obj\Debug\net8.0\Generated\Widget.g.cs(1,1): error CS2001: Source file 'D:\WorkCodes\Sample\App\obj\Debug\net8.0\Generated\Widget.g.cs' could not be found [D:\WorkCodes\Sample\App\App.csproj]",
            @"CSC : error CS0006: Metadata file 'D:\WorkCodes\Sample\App\bin\Debug\net8.0\App.dll' could not be found [D:\WorkCodes\Sample\App.Tests\App.Tests.csproj]");

        var result = await tools.AnalyzeCSharpBuildErrors(
            buildOutput,
            changedFiles: new[] { @"D:\WorkCodes\Sample\App\Feature.cs" },
            maxResults: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, bridge.CallCount);
        var report = Assert.Single(result.Items);
        Assert.Equal(5, report.TotalIssueCount);
        var topIssue = report.Issues[0];
        Assert.Equal("CS0103", topIssue.Id);
        Assert.True(topIssue.IsInChangedFile);
        Assert.True(topIssue.IsRootCauseCandidate);
        Assert.True(topIssue.RootCauseScore > 0);
        Assert.NotEmpty(topIssue.RankReasons);
        Assert.Contains(topIssue.RankReasons, reason => reason.Contains("root cause", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(topIssue.RecommendedNextActions, action =>
            action.Kind == WorkflowActionKind.InspectDiagnostic
            && action.SuggestedTool == "get_csharp_enclosing_context");
        Assert.Contains(report.Issues, issue => issue.Id == "NU1101" && issue.Kind == BuildIssueKind.Restore);
        Assert.Contains(report.Issues, issue => issue.Id == "NU1202" && issue.Kind == BuildIssueKind.TargetFramework);
        Assert.Contains(report.Issues, issue => issue.Id == "CS2001" && issue.Kind == BuildIssueKind.GeneratedOutput);
        var cascade = Assert.Single(report.Issues, issue => issue.Id == "CS0006");
        Assert.True(cascade.IsLikelyCascade);
        Assert.False(cascade.IsRootCauseCandidate);
        Assert.True(cascade.RootCauseScore < topIssue.RootCauseScore);
        Assert.Contains(cascade.RankReasons, reason => reason.Contains("cascade", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cascade.RecommendedNextActions, action => action.Kind == WorkflowActionKind.IgnoreBackgroundNoise);
        Assert.Contains(report.Issues.Where(issue => !issue.IsLikelyCascade), issue => issue.RecommendedNextActions.Length > 0);
        Assert.Contains(report.RecommendedNextActions, action => action.Kind == WorkflowActionKind.InspectDiagnostic);
        Assert.Contains(report.RecommendedNextActions, action => action.Kind == WorkflowActionKind.RunBuild);
        Assert.Contains(report.SuggestedNextSteps, step => step.Contains("restore failures", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.SuggestedNextSteps, step => step.Contains("target-framework failures", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.SuggestedNextSteps, step => step.Contains("missing generated outputs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvestigateCSharpBuildFailure_ReturnsBuildFailureSessionAndEvidencePacket()
    {
        var evidenceStore = new EvidenceStore();
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(
                new VisualStudioBridgeInstanceDescriptor
                {
                    InstanceId = "instance-a",
                    PipeName = "pipe-a",
                    SolutionPath = @"D:\A\A.sln",
                    IsAlive = true,
                }),
            WorkspaceStatusResult = QueryResult(
                new WorkspaceStatus
                {
                    InstanceId = "instance-a",
                    IsSolutionLoaded = true,
                    SolutionPath = @"D:\A\A.sln",
                    ProjectCount = 2,
                    DocumentCount = 20,
                }),
        };
        bridge.ProjectGraphResult = QueryResult(
            new ProjectGraph
            {
                Nodes = new[]
                {
                    new ProjectGraphNode
                    {
                        ProjectId = "feature-project",
                        ProjectName = "Feature.Project",
                        FilePath = @"D:\A\Feature.Project.csproj",
                    },
                    new ProjectGraphNode
                    {
                        ProjectId = "feature-tests",
                        ProjectName = "Feature.Project.Tests",
                        FilePath = @"D:\A\tests\Feature.Project.Tests\Feature.Project.Tests.csproj",
                    },
                },
            });
        bridge.SourceContextResult = QueryResult(
            new SourceContextSnippet
            {
                ProjectName = "Feature.Project",
                FilePath = @"D:\A\src\Feature\Widget.cs",
                ContextKind = "Method",
                Symbol = new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Feature.Widget.Create"),
                    Name = "Create",
                    ContainingType = "Feature.Widget",
                    ProjectName = "Feature.Project",
                },
                FocusSpan = new SourceSpan
                {
                    FilePath = @"D:\A\src\Feature\Widget.cs",
                    StartLine = 8,
                    StartColumn = 5,
                    EndLine = 12,
                    EndColumn = 6,
                },
                SnippetSpan = new SourceSpan
                {
                    FilePath = @"D:\A\src\Feature\Widget.cs",
                    StartLine = 8,
                    StartColumn = 5,
                    EndLine = 12,
                    EndColumn = 6,
                },
                Text = "public WidgetState Create() => new();",
                Reasons = new[] { "enclosing method" },
            });
        bridge.RelatedTestsResult = QueryResult(
            new RelatedTestDescriptor
            {
                ProjectName = "Feature.Project.Tests",
                TestClass = "WidgetTests",
                TestMethod = "Create_returns_state",
                TestSymbol = new SymbolDescriptor
                {
                    Name = "Create_returns_state",
                    ContainingType = "WidgetTests",
                    ProjectName = "Feature.Project.Tests",
                },
                Span = new SourceSpan
                {
                    FilePath = @"D:\A\tests\Feature.Project.Tests\WidgetTests.cs",
                    StartLine = 20,
                    StartColumn = 17,
                    EndLine = 20,
                    EndColumn = 37,
                },
                MatchReasons = new[] { "ReferenceMatch" },
            });
        var tools = new CodeNavigationTools(
            bridge,
            evidencePacketBuilder: new EvidencePacketBuilder(evidenceStore));
        var buildOutput = string.Join(
            Environment.NewLine,
            @"D:\A\src\Feature\Widget.cs(10,17): error CS0246: The type or namespace name 'WidgetState' could not be found [D:\A\Feature.Project.csproj]",
            @"CSC : error CS0006: Metadata file 'D:\A\bin\Debug\Missing.dll' could not be found [D:\A\App.csproj]");

        var result = await tools.InvestigateCSharpBuildFailure(
            problemText: "Investigate widget build failure",
            buildOutput: buildOutput,
            changedFiles: new[] { @"D:\A\src\Feature\Widget.cs" },
            targetInstanceId: "instance-a",
            includeVisualStudioBuildOutput: false,
            maxErrorListItems: 0,
            maxSourceSnippets: 0,
            cancellationToken: CancellationToken.None);

        var context = Assert.Single(result.Items);
        Assert.Equal("Ready", context.Status);
        Assert.Equal(AgentWorkflowTaskKind.BuildFailure, context.EvidencePacket.TaskKind);
        Assert.NotEmpty(context.EvidencePacket.PrimaryFindings);
        Assert.NotEmpty(context.BuildFailureSession.RootCauseCandidates);
        Assert.NotEmpty(context.BuildFailureSession.CascadeIssues);
        var binding = Assert.Single(context.BuildFailureSession.IssueBindings);
        Assert.Equal("CS0246", binding.IssueId);
        Assert.Equal("Feature.Project", binding.BoundProjectName);
        Assert.Equal("Create", binding.EnclosingSymbol?.Name);
        Assert.Contains(binding.BindingReasons, reason => reason.Contains("enclosing symbol", StringComparison.OrdinalIgnoreCase));
        Assert.Single(binding.RelatedTests);
        Assert.Contains(context.BuildFailureSession.ProjectBindings, project => project.ProjectName == "Feature.Project");
        Assert.Contains(context.BuildFailureSession.RelatedTests, test => test.TestMethod == "Create_returns_state");
        Assert.Contains(context.BuildFailureSession.RecommendedCommands, command =>
            command.Command.Contains("dotnet build", StringComparison.OrdinalIgnoreCase)
            && command.Command.Contains("Feature.Project.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.BuildFailureSession.RecommendedCommands, command =>
            command.Command.Contains("dotnet test", StringComparison.OrdinalIgnoreCase)
            && command.Command.Contains("Create_returns_state", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.BuildFailureSession.RecommendedNextActions, action =>
            action.SuggestedTool == "shell"
            && action.SuggestedCommand.Contains("dotnet build", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.EvidencePacket.ResourceLinks, link => link.Kind == "build-failure");
        Assert.Contains(context.EvidencePacket.ResourceLinks, link => link.Kind == "build-failure-json");
        Assert.Contains(context.EvidencePacket.ResourceLinks, link => link.Kind == "build-log");
        Assert.Contains(evidenceStore.ListResources(), resource => resource.Uri.Contains("/build-failure", StringComparison.Ordinal));
        Assert.Contains(evidenceStore.ListResources(), resource => resource.Uri.Contains("/build-log/", StringComparison.Ordinal));
        var buildFailureResource = Assert.Single(
            evidenceStore.ListResources(),
            resource => resource.Uri.EndsWith("/build-failure", StringComparison.Ordinal));
        var buildFailureRead = evidenceStore.ReadResource(buildFailureResource.Uri);
        var buildFailureText = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(
            Assert.Single(buildFailureRead.Contents)).Text;
        Assert.Contains("Issue bindings: 1", buildFailureText);
        Assert.Contains("Project: Feature.Project", buildFailureText);
        Assert.Contains("Recommended commands:", buildFailureText);
        Assert.Contains("dotnet build", buildFailureText);
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenBuildLogFilePathIsProvided_ReadsFile()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);
        var logFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                logFile,
                @"D:\WorkCodes\Sample\src\Feature\Widget.cs(10,17): error CS0246: The type or namespace name 'WidgetState' could not be found [D:\WorkCodes\Sample\Feature.csproj]");

            var result = await tools.AnalyzeCSharpBuildErrors(
                buildLogFilePath: logFile,
                changedFiles: new[] { @"src\Feature\Widget.cs" },
                cancellationToken: CancellationToken.None);

            Assert.Equal(0, bridge.CallCount);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("BuildLogFileRead"));
            var report = Assert.Single(result.Items);
            Assert.Single(report.Issues);
            Assert.Equal("CS0246", report.Issues[0].Id);
            Assert.True(report.Issues[0].IsInChangedFile);
        }
        finally
        {
            File.Delete(logFile);
        }
    }

    [Fact]
    public async Task AnalyzeCSharpBuildErrors_WhenNoBuildInputIsProvided_ReturnsDiagnostic()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.AnalyzeCSharpBuildErrors(cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Provide buildOutput"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task CollectArtifactEvidence_WhenLogContainsErrors_ReturnsCompactEvidence()
    {
        using var temp = TemporaryDirectory.Create();
        var logPath = Path.Combine(temp.Path, "run.log");
        await File.WriteAllTextAsync(
            logPath,
            string.Join(
                Environment.NewLine,
                "info: start",
                "warning: slow path",
                "error: import failed"));
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.CollectArtifactEvidence(
            artifactPaths: new[] { logPath },
            includeTextPatterns: new[] { "import" },
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        var artifact = Assert.Single(report.Artifacts);
        Assert.Equal(ArtifactEvidenceKind.Log, artifact.Kind);
        Assert.Contains("import failed", artifact.Summary);
        Assert.Contains(artifact.ErrorLines, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(artifact.WarningLines, line => line.Contains("warning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.RecommendedNextActions, action => action.Kind == WorkflowActionKind.InspectArtifact);
    }

    [Fact]
    public async Task WaitForArtifactEvidence_WhenArtifactAlreadyExists_ReturnsReadyEvidence()
    {
        using var temp = TemporaryDirectory.Create();
        var logPath = Path.Combine(temp.Path, "run.log");
        await File.WriteAllTextAsync(logPath, "error: scenario failed");
        var tools = new CodeNavigationTools(new RecordingBridge());

        var result = await tools.WaitForArtifactEvidence(
            artifactPaths: new[] { logPath },
            timeoutMilliseconds: 0,
            pollIntervalMilliseconds: 100,
            cancellationToken: CancellationToken.None);

        var report = Assert.Single(result.Items);
        Assert.Equal("Ready", report.Status);
        Assert.Single(report.Artifacts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("ArtifactWaitElapsedMilliseconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCSharpWorkflowPerformanceSnapshot_ReturnsBridgeBudgetAndTelemetryHints()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(new VisualStudioBridgeInstanceDescriptor
            {
                InstanceId = "vs-ready",
                SolutionPath = @"D:\A\Sample.sln",
                IsAlive = true,
                IsStale = false,
            }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.GetCSharpWorkflowPerformanceSnapshot(
            benchmarkProfile: "large",
            cancellationToken: CancellationToken.None);

        var snapshot = Assert.Single(result.Items);
        Assert.Equal("Ready", snapshot.BridgeStatus);
        Assert.Equal(@"D:\A\Sample.sln", snapshot.TargetSolutionPath);
        Assert.Equal(83, snapshot.ExpectedToolCount);
        Assert.Equal(83, snapshot.ToolCount);
        Assert.Contains("vs-ready", snapshot.ActiveInstanceIds);
        Assert.Contains("large", snapshot.RecommendedProfiles);
        Assert.Contains("agentic-resources", snapshot.RecommendedProfiles);
        Assert.Contains(snapshot.SuggestedEnvironmentVariables, value => value.Contains("CODE_NAVIGATOR_BENCHMARK_SOLUTION", StringComparison.Ordinal));
        Assert.Contains(snapshot.BudgetHints, hint => hint.Name == "ReturnedCharacters");
        Assert.Contains(snapshot.TelemetrySignals, signal => signal.Name == "ServerCacheHit");
    }

    [Fact]
    public async Task AnalyzeCSharpRepoWorkflow_AndGenerateInstructions_ReturnRepositoryOnboardingDraft()
    {
        using var temp = TemporaryDirectory.Create();
        var srcDir = Path.Combine(temp.Path, "src", "Sample.App");
        var testDir = Path.Combine(temp.Path, "tests", "Sample.App.Tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Sample.slnx"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Sample.App.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(testDir, "Sample.App.Tests.csproj"), "<Project />");
        var tools = new CodeNavigationTools(new RecordingBridge());

        var analysisResult = await tools.AnalyzeCSharpRepoWorkflow(
            rootDirectory: temp.Path,
            preferredName: "Sample",
            cancellationToken: CancellationToken.None);
        var analysis = Assert.Single(analysisResult.Items);

        Assert.Equal("Ready", analysis.Status);
        Assert.EndsWith("Sample.slnx", analysis.SelectedSolutionPath);
        Assert.Contains(analysis.TestProjectFiles, path => path.EndsWith("Sample.App.Tests.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.BuildCommands, command => command.Command.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains(analysis.NoisyPathPatterns, pattern => pattern.Contains("bin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(analysis.ToolRoutingRules, rule => rule.Contains("get_csharp_task_context", StringComparison.Ordinal));

        var draftResult = await tools.GenerateCSharpAgentInstructions(
            rootDirectory: temp.Path,
            preferredName: "Sample",
            cancellationToken: CancellationToken.None);
        var draft = Assert.Single(draftResult.Items);

        Assert.Equal("markdown", draft.Format);
        Assert.Contains("Visual Studio C# Dev Workflow", draft.Content, StringComparison.Ordinal);
        Assert.Contains("Sample.slnx", draft.Content, StringComparison.Ordinal);
        Assert.Contains("Agent work packets", draft.Sections);
    }

    [Fact]
    public async Task SplitCSharpAgentWork_AndMergeFindings_ReturnScopedPacketsAndMissingIds()
    {
        var tools = new CodeNavigationTools(new RecordingBridge());

        var splitResult = await tools.SplitCSharpAgentWork(
            objective: "review feature change",
            changedFiles: new[]
            {
                @"D:\Repo\src\Feature\Widget.cs",
                @"D:\Repo\tests\Feature.Tests\WidgetTests.cs",
            },
            scopeSymbols: new[] { "Feature.Widget" },
            evidenceResources: new[] { "mcp://evidence/context/1" },
            maxPackets: 4,
            cancellationToken: CancellationToken.None);
        var split = Assert.Single(splitResult.Items);

        Assert.Equal("Ready", split.Status);
        Assert.True(split.Packets.Length >= 2);
        Assert.All(split.Packets, packet => Assert.False(packet.Budget.AllowWholeSolution));
        Assert.Contains(split.Packets, packet => packet.ScopeFiles.Any(file => file.EndsWith("Widget.cs", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("packetId", split.ExpectedFindingSchema, StringComparison.Ordinal);

        var firstPacket = split.Packets[0].PacketId;
        var mergeResult = await tools.MergeCSharpAgentFindings(
            objective: split.Objective,
            expectedPacketIds: split.Packets.Select(packet => packet.PacketId).Append("packet-missing").ToArray(),
            findings: new[]
            {
                new CSharpAgentFinding
                {
                    PacketId = firstPacket,
                    Status = "Ready",
                    Summary = "Scoped review completed.",
                    Findings = new[] { "Widget.cs has focused coverage." },
                    RecommendedNextActions = new[] { "Run focused tests." },
                },
            },
            cancellationToken: CancellationToken.None);
        var merge = Assert.Single(mergeResult.Items);

        Assert.Equal("MissingPackets", merge.Status);
        Assert.Contains("packet-missing", merge.MissingPacketIds);
        Assert.Contains("Widget.cs has focused coverage.", merge.Findings);
        Assert.True(mergeResult.IsPartial);
    }

    [Fact]
    public async Task GetCSharpProjectGraph_WhenRepeated_UsesShortLivedCache()
    {
        var bridge = new RecordingBridge
        {
            ProjectGraphResult = QueryResult(new ProjectGraph()),
        };
        var tools = new CodeNavigationTools(bridge, new ShortLivedQueryCache(TimeSpan.FromMinutes(1)));

        var first = await tools.GetCSharpProjectGraph(projectName: "SampleWorkspace.Core", cancellationToken: CancellationToken.None);
        var second = await tools.GetCSharpProjectGraph(projectName: "SampleWorkspace.Core", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.DoesNotContain(first.Diagnostics, diagnostic => diagnostic.Contains("ServerCacheHit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Contains("ServerCacheHit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetCSharpProjectGraph_WhenRequestDiffers_DoesNotReuseCache()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge, new ShortLivedQueryCache(TimeSpan.FromMinutes(1)));

        await tools.GetCSharpProjectGraph(projectName: "SampleWorkspace.Core", cancellationToken: CancellationToken.None);
        await tools.GetCSharpProjectGraph(projectName: "SampleWorkspace.Tests", cancellationToken: CancellationToken.None);

        Assert.Equal(2, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpReferencesBySymbolSearch_WhenUniqueCandidateExists_UsesSymbolKey()
    {
        var bridge = new RecordingBridge
        {
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:M:Sample.ComponentDefinitionManager.Save"),
                    Name = "Save",
                    ContainingType = "ComponentDefinitionManager",
                    ProjectName = "SampleWorkspace.Core",
                    Kind = CodeSymbolKind.Method,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpReferencesBySymbolSearch(
            "Save",
            containingType: "ComponentDefinitionManager",
            projectName: "SampleWorkspace.Core",
            kind: CodeSymbolKind.Method,
            maxResults: 40,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, bridge.CallCount);
        Assert.NotNull(bridge.LastReferenceRequest);
        Assert.Equal("docid:M:Sample.ComponentDefinitionManager.Save", bridge.LastReferenceRequest.SymbolKey?.Value);
        Assert.Equal(40, bridge.LastReferenceRequest.MaxResults);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public async Task FindCSharpReferencesBySymbolSearch_WhenCandidatesAreAmbiguous_DoesNotGuess()
    {
        var bridge = new RecordingBridge
        {
            SearchResult = QueryResult(
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:T:Sample.UISystem"),
                    Name = "UISystem",
                    ProjectName = "SampleWorkspace.UI",
                    Kind = CodeSymbolKind.Type,
                },
                new SymbolDescriptor
                {
                    Key = new SymbolKey("docid:P:Sample.AppRuntime.UISystem"),
                    Name = "UISystem",
                    ContainingType = "AppRuntime",
                    ProjectName = "SampleWorkspace.Runtime",
                    Kind = CodeSymbolKind.Property,
                }),
        };
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpReferencesBySymbolSearch("UISystem", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.Null(bridge.LastReferenceRequest);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("AmbiguousSymbolSearch"));
    }

    [Fact]
    public async Task FindCSharpImplementations_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpImplementations(
            "docid:T:SampleWorkspace.Sample.IFoo",
            maxResults: 30,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastImplementationRequest);
        Assert.Equal("docid:T:SampleWorkspace.Sample.IFoo", bridge.LastImplementationRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastImplementationRequest.Position);
        Assert.Equal(30, bridge.LastImplementationRequest.MaxResults);
    }

    [Fact]
    public async Task FindCSharpOverrides_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpOverrides(
            "docid:M:SampleWorkspace.Sample.Base.Draw",
            maxResults: 35,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastOverrideRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.Base.Draw", bridge.LastOverrideRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastOverrideRequest.Position);
        Assert.Equal(35, bridge.LastOverrideRequest.MaxResults);
    }

    [Fact]
    public async Task AnalyzeCSharpSymbolImpact_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.AnalyzeCSharpSymbolImpact("docid:M:SampleWorkspace.Sample.SetProps", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastImpactRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastImpactRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastImpactRequest.Position);
        Assert.Equal(1, bridge.LastImpactRequest.MaxDepth);
        Assert.Equal(1000, bridge.LastImpactRequest.MaxResults);
        Assert.Equal(20, bridge.LastImpactRequest.MaxProjects);
        Assert.Equal(20, bridge.LastImpactRequest.MaxFiles);
        Assert.Equal(20, bridge.LastImpactRequest.MaxContainingTypes);
    }

    [Fact]
    public async Task AnalyzeCSharpSymbolImpact_WhenMaxDepthIsGreaterThanOne_ForwardsRecursiveRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.AnalyzeCSharpSymbolImpact(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxDepth: 2,
            maxResults: 250,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastImpactRequest);
        Assert.Equal(2, bridge.LastImpactRequest.MaxDepth);
        Assert.Equal(250, bridge.LastImpactRequest.MaxResults);
    }

    [Fact]
    public async Task FindCSharpCallers_WhenMaxDepthIsGreaterThanOne_ForwardsRecursiveRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpCallers(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxDepth: 2,
            maxResults: 25,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastCallersRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastCallersRequest.SymbolKey?.Value);
        Assert.Equal(2, bridge.LastCallersRequest.MaxDepth);
        Assert.Equal(25, bridge.LastCallersRequest.MaxResults);
    }

    [Fact]
    public async Task FindCSharpCallees_WhenMaxDepthIsGreaterThanOne_ForwardsRecursiveRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpCallees(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxDepth: 3,
            maxResults: 30,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastCalleesRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastCalleesRequest.SymbolKey?.Value);
        Assert.Equal(3, bridge.LastCalleesRequest.MaxDepth);
        Assert.Equal(30, bridge.LastCalleesRequest.MaxResults);
    }

    [Fact]
    public async Task FindCSharpCallers_WhenMaxDepthIsTooLarge_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpCallers(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxDepth: 11,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxDepth"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpRelatedTests_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpRelatedTests("docid:M:SampleWorkspace.Sample.SetProps", cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastRelatedTestsRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastRelatedTestsRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastRelatedTestsRequest.Position);
        Assert.Null(bridge.LastRelatedTestsRequest.FilePath);
        Assert.Equal(100, bridge.LastRelatedTestsRequest.MaxResults);
    }

    [Fact]
    public async Task FindCSharpRelatedTests_WhenOnlyFilePathIsProvided_CallsBridgeWithFileLevelRequest()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.FindCSharpRelatedTests(
            filePath: @"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\LcObject.cs",
            maxResults: 25,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastRelatedTestsRequest);
        Assert.Null(bridge.LastRelatedTestsRequest.SymbolKey);
        Assert.Null(bridge.LastRelatedTestsRequest.Position);
        Assert.Equal(@"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\LcObject.cs", bridge.LastRelatedTestsRequest.FilePath);
        Assert.Equal(25, bridge.LastRelatedTestsRequest.MaxResults);
    }

    [Fact]
    public async Task PreviewCSharpRename_WhenOnlySymbolKeyIsProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.PreviewCSharpRename(
            "RenamedSetProps",
            "docid:M:SampleWorkspace.Sample.SetProps",
            renameOverloads: true,
            renameInStrings: true,
            renameInComments: true,
            renameFile: true,
            maxTextChanges: 25,
            maxSnippetLength: 50,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastRenamePreviewRequest);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastRenamePreviewRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastRenamePreviewRequest.Position);
        Assert.Equal("RenamedSetProps", bridge.LastRenamePreviewRequest.NewName);
        Assert.True(bridge.LastRenamePreviewRequest.RenameOverloads);
        Assert.True(bridge.LastRenamePreviewRequest.RenameInStrings);
        Assert.True(bridge.LastRenamePreviewRequest.RenameInComments);
        Assert.True(bridge.LastRenamePreviewRequest.RenameFile);
        Assert.Equal(25, bridge.LastRenamePreviewRequest.MaxTextChanges);
        Assert.Equal(50, bridge.LastRenamePreviewRequest.MaxSnippetLength);
    }

    [Fact]
    public async Task ApplyCSharpRename_WhenOnlySymbolKeyAndTargetAreProvided_CallsBridge()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.ApplyCSharpRename(
            "RenamedSetProps",
            "docid:M:SampleWorkspace.Sample.SetProps",
            renameOverloads: true,
            renameInStrings: true,
            renameInComments: true,
            renameFile: true,
            maxTextChanges: 25,
            maxSnippetLength: 50,
            includeGeneratedCode: true,
            allowConflicts: true,
            allowGeneratedDocumentChanges: true,
            allowUnsupportedDocumentChanges: true,
            allowTruncatedPreview: true,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastRenameApplyRequest);
        Assert.Equal("instance-a", bridge.LastRenameApplyRequest.Target?.InstanceId);
        Assert.Equal("docid:M:SampleWorkspace.Sample.SetProps", bridge.LastRenameApplyRequest.SymbolKey?.Value);
        Assert.Null(bridge.LastRenameApplyRequest.Position);
        Assert.Equal("RenamedSetProps", bridge.LastRenameApplyRequest.NewName);
        Assert.True(bridge.LastRenameApplyRequest.RenameOverloads);
        Assert.True(bridge.LastRenameApplyRequest.RenameInStrings);
        Assert.True(bridge.LastRenameApplyRequest.RenameInComments);
        Assert.True(bridge.LastRenameApplyRequest.RenameFile);
        Assert.Equal(25, bridge.LastRenameApplyRequest.MaxTextChanges);
        Assert.Equal(50, bridge.LastRenameApplyRequest.MaxSnippetLength);
        Assert.True(bridge.LastRenameApplyRequest.IncludeGeneratedCode);
        Assert.True(bridge.LastRenameApplyRequest.AllowConflicts);
        Assert.True(bridge.LastRenameApplyRequest.AllowGeneratedDocumentChanges);
        Assert.True(bridge.LastRenameApplyRequest.AllowUnsupportedDocumentChanges);
        Assert.True(bridge.LastRenameApplyRequest.AllowTruncatedPreview);
    }

    [Fact]
    public async Task ApplyCSharpRename_WhenTargetIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.ApplyCSharpRename(
            "RenamedSetProps",
            "docid:M:SampleWorkspace.Sample.SetProps",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("explicit target"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ApplyCSharpRename_WhenNewNameIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.ApplyCSharpRename(
            " ",
            "docid:M:SampleWorkspace.Sample.SetProps",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("NewName"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpDefinitions_WhenIdentifierIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpDefinitions(null, null, null, null, cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("symbol key"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpReferences_WhenPositionIsNotOneBased_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpReferences(
            null,
            @"D:\Samples\SampleWorkspace\SomeFile.cs",
            0,
            1,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("one-based"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpImplementations_WhenIdentifierIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpImplementations(null, null, null, null, cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("symbol key"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpReferences_WhenSymbolKeyHasPartialPosition_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpReferences(
            "M:SampleWorkspace.Sample.SetProps",
            @"D:\Samples\SampleWorkspace\SomeFile.cs",
            null,
            null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Source position"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task AnalyzeCSharpSymbolImpact_WhenMaxResultsIsInvalid_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.AnalyzeCSharpSymbolImpact(
            "docid:M:SampleWorkspace.Sample.SetProps",
            maxResults: 0,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MaxResults"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task FindCSharpRelatedTests_WhenIdentifierIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.FindCSharpRelatedTests(cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("symbol key"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task PreviewCSharpRename_WhenNewNameIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PreviewCSharpRename(
            " ",
            "docid:M:SampleWorkspace.Sample.SetProps",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("NewName"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task PreviewCSharpRename_WhenIdentifierIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.PreviewCSharpRename("RenamedSetProps", cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("symbol key"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ContinueDebugging_WhenTargetIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.ContinueDebugging(cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("explicit target"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ContinueDebugging_WhenTargetIsProvided_ForwardsTargetAndTimeout()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.ContinueDebugging(
            targetSolutionPath: @"D:\WorkCodes\Sample\Sample.sln",
            timeoutMilliseconds: 1234,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastDebugControlRequest);
        Assert.Equal(DebugControlAction.Continue, bridge.LastDebugControlRequest.Action);
        Assert.Equal(@"D:\WorkCodes\Sample\Sample.sln", bridge.LastDebugControlRequest.Target?.SolutionPath);
        Assert.Equal(1234, bridge.LastDebugControlRequest.TimeoutMilliseconds);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenSourceIsProvided_ForwardsBreakpointMutation()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            condition: "value > 0",
            conditionMode: DebugBreakpointConditionMode.WhenChanged,
            hitCountTarget: 3,
            hitCountMode: DebugBreakpointHitCountMode.GreaterOrEqual,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastBreakpointMutationRequest);
        Assert.Equal(DebugControlAction.SetBreakpoint, bridge.LastBreakpointMutationRequest.Action);
        Assert.Equal(@"D:\WorkCodes\Sample\Program.cs", bridge.LastBreakpointMutationRequest.FilePath);
        Assert.Equal(12, bridge.LastBreakpointMutationRequest.Line);
        Assert.Equal(3, bridge.LastBreakpointMutationRequest.Column);
        Assert.Equal("value > 0", bridge.LastBreakpointMutationRequest.Condition);
        Assert.Equal(DebugBreakpointConditionMode.WhenChanged, bridge.LastBreakpointMutationRequest.ConditionMode);
        Assert.Equal(3, bridge.LastBreakpointMutationRequest.HitCountTarget);
        Assert.Equal(DebugBreakpointHitCountMode.GreaterOrEqual, bridge.LastBreakpointMutationRequest.HitCountMode);
        Assert.Equal("instance-a", bridge.LastBreakpointMutationRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenConditionModeIsOmitted_DefaultsToWhenTrue()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            condition: "value > 0",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastBreakpointMutationRequest);
        Assert.Equal("value > 0", bridge.LastBreakpointMutationRequest.Condition);
        Assert.Equal(DebugBreakpointConditionMode.WhenTrue, bridge.LastBreakpointMutationRequest.ConditionMode);
        Assert.Equal(0, bridge.LastBreakpointMutationRequest.HitCountTarget);
        Assert.Equal(DebugBreakpointHitCountMode.None, bridge.LastBreakpointMutationRequest.HitCountMode);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenConditionModeIsNoneWithCondition_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            condition: "value > 0",
            conditionMode: DebugBreakpointConditionMode.None,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("conditionMode"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenConditionModeIsWhenChangedWithoutCondition_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            conditionMode: DebugBreakpointConditionMode.WhenChanged,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("condition"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenHitCountTargetIsProvidedWithoutMode_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            hitCountTarget: 2,
            hitCountMode: DebugBreakpointHitCountMode.None,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("hitCountTarget"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenHitCountModeIsProvidedWithoutTarget_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            hitCountMode: DebugBreakpointHitCountMode.Equal,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("hitCountTarget"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task SetDebugBreakpoint_WhenHitCountTargetIsNegative_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.SetDebugBreakpoint(
            @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            column: 3,
            hitCountTarget: -1,
            hitCountMode: DebugBreakpointHitCountMode.Equal,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("hitCountTarget"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task RemoveDebugBreakpoint_WhenColumnIsOmitted_ForwardsLineOnlySelector()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.RemoveDebugBreakpoint(
            filePath: @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            targetSolutionPath: @"D:\WorkCodes\Sample\Sample.sln",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastBreakpointMutationRequest);
        Assert.Equal(DebugControlAction.RemoveBreakpoint, bridge.LastBreakpointMutationRequest.Action);
        Assert.Equal(@"D:\WorkCodes\Sample\Program.cs", bridge.LastBreakpointMutationRequest.FilePath);
        Assert.Equal(12, bridge.LastBreakpointMutationRequest.Line);
        Assert.Equal(0, bridge.LastBreakpointMutationRequest.Column);
    }

    [Fact]
    public async Task EnableDebugBreakpoint_WhenColumnIsOmitted_ForwardsLineOnlySelector()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        await tools.EnableDebugBreakpoint(
            enabled: false,
            filePath: @"D:\WorkCodes\Sample\Program.cs",
            line: 12,
            targetSolutionPath: @"D:\WorkCodes\Sample\Sample.sln",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastBreakpointMutationRequest);
        Assert.Equal(DebugControlAction.EnableBreakpoint, bridge.LastBreakpointMutationRequest.Action);
        Assert.False(bridge.LastBreakpointMutationRequest.Enabled);
        Assert.Equal(@"D:\WorkCodes\Sample\Program.cs", bridge.LastBreakpointMutationRequest.FilePath);
        Assert.Equal(12, bridge.LastBreakpointMutationRequest.Line);
        Assert.Equal(0, bridge.LastBreakpointMutationRequest.Column);
    }

    [Fact]
    public async Task RemoveDebugBreakpoint_WhenSelectorIsMissing_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeNavigationTools(bridge);

        var result = await tools.RemoveDebugBreakpoint(
            targetSolutionPath: @"D:\WorkCodes\Sample\Sample.sln",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("breakpointName"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task PreviewCSharpCodeFix_ForwardsMutationCandidateIdentityFields()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeFixTools(bridge);

        await tools.PreviewCSharpCodeFix(
            diagnosticId: "CS0103",
            filePath: @"D:\A\src\Feature\Widget.cs",
            fixTitle: "Generate property",
            providerName: "CSharpGenerateMemberCodeFixProvider",
            equivalenceKey: "GenerateProperty",
            candidateStableKey: "CS0103|provider|GenerateProperty|Generate property|Document|Widget.cs",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastCodeFixRequest);
        Assert.Equal("CS0103", bridge.LastCodeFixRequest.DiagnosticId);
        Assert.Equal("Generate property", bridge.LastCodeFixRequest.FixTitle);
        Assert.Equal("CSharpGenerateMemberCodeFixProvider", bridge.LastCodeFixRequest.ProviderName);
        Assert.Equal("GenerateProperty", bridge.LastCodeFixRequest.EquivalenceKey);
        Assert.Equal("CS0103|provider|GenerateProperty|Generate property|Document|Widget.cs", bridge.LastCodeFixRequest.CandidateStableKey);
        Assert.Equal("instance-a", bridge.LastCodeFixRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task ApplyCSharpCodeFix_WithoutPreviewSession_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeFixTools(bridge);

        var result = await tools.ApplyCSharpCodeFix(
            diagnosticId: "CS0103",
            filePath: @"D:\A\src\Feature\Widget.cs",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MutationSessionRequired"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ApplyCSharpCodeFix_WithPreviewSession_ReplaysPreviewBeforeApply()
    {
        var bridge = new RecordingBridge();
        var store = new MutationSessionStore();
        var tools = new CodeFixTools(bridge, store);
        var preview = CreateCodeFixPreview("mutation:codefix:CS0103:ABC", "workspace-v1");
        bridge.CodeFixPreviewResult = new WorkspaceQueryResult<CSharpCodeFixPreview>
        {
            Items = new[] { preview },
        };

        await tools.PreviewCSharpCodeFix(
            diagnosticId: "CS0103",
            filePath: @"D:\A\src\Feature\Widget.cs",
            cancellationToken: CancellationToken.None);

        var result = await tools.ApplyCSharpCodeFix(
            diagnosticId: "CS0103",
            filePath: @"D:\A\src\Feature\Widget.cs",
            previewSessionId: "mutation:codefix:CS0103:ABC",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsPartial);
        Assert.Equal(3, bridge.CallCount);
        Assert.NotNull(bridge.LastCodeFixRequest);
        Assert.Equal("mutation:codefix:CS0103:ABC", bridge.LastCodeFixRequest.PreviewSessionId);
        Assert.Equal("workspace-v1", bridge.LastCodeFixRequest.ExpectedWorkspaceVersion);
        Assert.Equal("CSharpGenerateMemberCodeFixProvider", bridge.LastCodeFixRequest.ProviderName);
        Assert.Equal("GenerateProperty", bridge.LastCodeFixRequest.EquivalenceKey);
        Assert.Equal("stable-key", bridge.LastCodeFixRequest.CandidateStableKey);
    }

    [Fact]
    public async Task ApplyCSharpFixAll_WithoutPreviewSession_ReturnsDiagnosticWithoutBridgeCall()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeFixTools(bridge);

        var result = await tools.ApplyCSharpFixAll(
            diagnosticId: "CS0103",
            scopeKind: CSharpMutationScopeKind.Project,
            projectName: "Sample.Project",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("MutationSessionRequired"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ApplyCSharpFixAll_WithPreviewSession_ReplaysPreviewBeforeApply()
    {
        var bridge = new RecordingBridge();
        var store = new MutationSessionStore();
        var tools = new CodeFixTools(bridge, store);
        var preview = CreateCodeFixPreview("mutation:fixall:CS0103:ABC", "workspace-v1", CSharpMutationScopeKind.Project);
        bridge.FixAllPreviewResult = new WorkspaceQueryResult<CSharpCodeFixPreview>
        {
            Items = new[] { preview },
        };

        await tools.PreviewCSharpFixAll(
            diagnosticId: "CS0103",
            scopeKind: CSharpMutationScopeKind.Project,
            projectName: "Sample.Project",
            cancellationToken: CancellationToken.None);

        var result = await tools.ApplyCSharpFixAll(
            diagnosticId: "CS0103",
            scopeKind: CSharpMutationScopeKind.Project,
            projectName: "Sample.Project",
            previewSessionId: "mutation:fixall:CS0103:ABC",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsPartial);
        Assert.Equal(3, bridge.CallCount);
        Assert.NotNull(bridge.LastCodeFixRequest);
        Assert.Equal("mutation:fixall:CS0103:ABC", bridge.LastCodeFixRequest.PreviewSessionId);
        Assert.Equal("workspace-v1", bridge.LastCodeFixRequest.ExpectedWorkspaceVersion);
        Assert.Equal("CSharpGenerateMemberCodeFixProvider", bridge.LastCodeFixRequest.ProviderName);
        Assert.Equal("GenerateProperty", bridge.LastCodeFixRequest.EquivalenceKey);
        Assert.Equal("stable-key", bridge.LastCodeFixRequest.CandidateStableKey);
        Assert.Equal(CSharpMutationScopeKind.Project, bridge.LastCodeFixRequest.ScopeKind);
        Assert.Equal("Sample.Project", bridge.LastCodeFixRequest.ProjectName);
    }

    [Fact]
    public async Task ListCSharpCodeFixes_ForwardsScopeFilters()
    {
        var bridge = new RecordingBridge();
        var tools = new CodeFixTools(bridge);

        await tools.ListCSharpCodeFixes(
            scopeKind: CSharpMutationScopeKind.ChangedFiles,
            changedFiles: new[] { @"src\Feature\Widget.cs" },
            includePathPatterns: new[] { @"src\Feature", @"tests\*.cs" },
            excludePathPatterns: new[] { @"src\ACADPlugins", @"TZData_src" },
            diagnosticId: "CS0103",
            noiseProfile: CodeDiagnosticNoiseProfile.Filter,
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, bridge.CallCount);
        Assert.NotNull(bridge.LastCodeFixListRequest);
        Assert.Equal(CSharpMutationScopeKind.ChangedFiles, bridge.LastCodeFixListRequest.ScopeKind);
        Assert.Equal(new[] { @"src\Feature\Widget.cs" }, bridge.LastCodeFixListRequest.ChangedFiles);
        Assert.Equal(new[] { @"src\Feature", @"tests\*.cs" }, bridge.LastCodeFixListRequest.IncludePathPatterns);
        Assert.Equal(new[] { @"src\ACADPlugins", @"TZData_src" }, bridge.LastCodeFixListRequest.ExcludePathPatterns);
        Assert.Equal("CS0103", bridge.LastCodeFixListRequest.DiagnosticId);
        Assert.Equal(CodeDiagnosticNoiseProfile.Filter, bridge.LastCodeFixListRequest.NoiseProfile);
        Assert.Equal("instance-a", bridge.LastCodeFixListRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task PreviewCSharpRefactoringPlan_ForRename_ReturnsExecutableSteps()
    {
        var bridge = new RecordingBridge
        {
            RefactoringPlanResult = QueryResult(
                new CSharpRefactoringPlan
                {
                    Kind = CSharpRefactoringPlanKind.Rename,
                    Summary = "Use Roslyn symbol rename preview/apply for this refactoring.",
                    IsSupportedForPreview = true,
                    IsSupportedForApply = true,
                    RecommendedTools = new[] { "preview_csharp_rename", "apply_csharp_rename" },
                }),
        };
        var tools = new RefactoringTools(new CodeNavigationTools(bridge), bridge);

        var result = await tools.PreviewCSharpRefactoringPlan(
            CSharpRefactoringPlanKind.Rename,
            symbolKey: "docid:P:Feature.Widget.Name",
            newName: "DisplayName",
            targetInstanceId: "instance-a",
            cancellationToken: CancellationToken.None);

        var plan = Assert.Single(result.Items);
        Assert.Contains(plan.ExecutionSteps, step =>
            step.ToolName == "preview_csharp_rename"
            && !step.IsMutating
            && !step.RequiresUserApproval);
        Assert.Contains(plan.ExecutionSteps, step =>
            step.ToolName == "apply_csharp_rename"
            && step.IsMutating
            && step.RequiresUserApproval);
        Assert.DoesNotContain(plan.Blockers, blocker => blocker.Code == "MissingNewName");
        Assert.NotNull(bridge.LastRefactoringPlanRequest);
        Assert.Equal("DisplayName", bridge.LastRefactoringPlanRequest.NewName);
        Assert.Equal("instance-a", bridge.LastRefactoringPlanRequest.Target?.InstanceId);
    }

    [Fact]
    public async Task PreviewCSharpRefactoringPlan_ForPlanOnlyRefactor_ReturnsManualEditRouteAndMissingTargetBlocker()
    {
        var bridge = new RecordingBridge
        {
            RefactoringPlanResult = QueryResult(
                new CSharpRefactoringPlan
                {
                    Kind = CSharpRefactoringPlanKind.ExtractMethod,
                    Summary = "Extract Method is plan-only.",
                    IsSupportedForPreview = false,
                    IsSupportedForApply = false,
                    RecommendedTools = new[] { "get_csharp_source_context", "analyze_csharp_symbol_impact", "plan_csharp_verification" },
                    Blockers = new[]
                    {
                        new WorkspaceMutationBlocker
                        {
                            Kind = WorkspaceMutationBlockerKind.UnsupportedOperation,
                            Code = "ComplexRefactoringPlanOnly",
                            Message = "This refactoring is plan-only.",
                        },
                    },
                }),
        };
        var tools = new RefactoringTools(new CodeNavigationTools(bridge), bridge);

        var result = await tools.PreviewCSharpRefactoringPlan(
            CSharpRefactoringPlanKind.ExtractMethod,
            filePath: @"D:\A\src\Feature\Widget.cs",
            cancellationToken: CancellationToken.None);

        var plan = Assert.Single(result.Items);
        Assert.Contains(plan.ExecutionSteps, step => step.ToolName == "get_csharp_source_context");
        Assert.Contains(plan.ExecutionSteps, step => step.ToolName == "normal_source_edit" && step.IsMutating);
        Assert.Contains(plan.ExecutionSteps, step => step.ToolName == "plan_csharp_verification");
        Assert.Contains(plan.Blockers, blocker => blocker.Code == "ComplexRefactoringPlanOnly");
        Assert.Contains(plan.Blockers, blocker => blocker.Code == "MissingSymbolTarget");
    }

    private static CSharpCodeFixPreview CreateCodeFixPreview(
        string sessionId,
        string workspaceVersion,
        CSharpMutationScopeKind scopeKind = CSharpMutationScopeKind.Document)
    {
        return new CSharpCodeFixPreview
        {
            DiagnosticId = "CS0103",
            FixTitle = "Generate property",
            ScopeKind = scopeKind,
            MutationPreview = new WorkspaceMutationPreview
            {
                SessionId = sessionId,
                WorkspaceVersion = workspaceVersion,
                CandidateIdentity = new MutationCandidateIdentity
                {
                    DiagnosticId = "CS0103",
                    ProviderName = "CSharpGenerateMemberCodeFixProvider",
                    EquivalenceKey = "GenerateProperty",
                    Title = "Generate property",
                    Scope = scopeKind.ToString(),
                    DocumentOrProject = @"D:\A\src\Feature\Widget.cs",
                    StableKey = "stable-key",
                },
            },
        };
    }

    private static void AssertOptionalNull(ParameterInfo parameter)
    {
        Assert.True(parameter.IsOptional);
        Assert.Null(parameter.DefaultValue);
    }

    private static WorkflowKernel CreateWorkflowKernel(RecordingBridge bridge, EvidenceStore? evidenceStore = null)
    {
        return new WorkflowKernel(
            new CodeNavigationTools(bridge),
            new EvidencePacketBuilder(evidenceStore ?? new EvidenceStore()),
            new WorkflowBudgetPolicy(),
            new WorkflowSafetyGate(),
            new TaskRouter(),
            new WorkflowTelemetryRecorder(),
            new WorkspaceContextLeaseStore());
    }

    private sealed class RecordingBridge : IVisualStudioWorkspaceBridge
    {
        public WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>? InstancesResult { get; set; }

        public Queue<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>>? InstancesResults { get; set; }

        public WorkspaceQueryResult<WorkspaceStatus>? WorkspaceStatusResult { get; set; }

        public WorkspaceQueryResult<SymbolDescriptor>? SearchResult { get; set; }

        public WorkspaceQueryResult<SymbolDescriptor>? DefinitionResult { get; set; }

        public WorkspaceQueryResult<SymbolReference>? ReferenceResult { get; set; }

        public WorkspaceQueryResult<CodeDiagnostic>? DiagnosticsResult { get; set; }

        public WorkspaceQueryResult<ProjectGraph>? ProjectGraphResult { get; set; }

        public WorkspaceQueryResult<CallGraphEdge>? CallersResult { get; set; }

        public WorkspaceQueryResult<CallGraphEdge>? CalleesResult { get; set; }

        public WorkspaceQueryResult<SymbolImpactSummary>? ImpactResult { get; set; }

        public WorkspaceQueryResult<RelatedTestDescriptor>? RelatedTestsResult { get; set; }

        public WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>? OutputWindowResult { get; set; }

        public WorkspaceQueryResult<VisualStudioDocumentSnapshot>? OpenDocumentsResult { get; set; }

        public WorkspaceQueryResult<VisualStudioDocumentSnapshot>? ActiveDocumentResult { get; set; }

        public WorkspaceQueryResult<SourceNavigationResult>? SourceNavigationResult { get; set; }

        public WorkspaceQueryResult<TemporaryMarker>? TemporaryMarkersResult { get; set; }

        public WorkspaceQueryResult<SourceContextSnippet>? SourceContextResult { get; set; }

        public Queue<WorkspaceQueryResult<SourceContextSnippet>>? SourceContextResults { get; set; }

        public WorkspaceQueryResult<SourceContextSnippet>? SymbolSourceResult { get; set; }

        public Queue<WorkspaceQueryResult<SourceContextSnippet>>? SymbolSourceResults { get; set; }

        public WorkspaceQueryResult<CSharpCodeFixPreview>? CodeFixPreviewResult { get; set; }

        public WorkspaceQueryResult<CSharpCodeFixApplyResult>? CodeFixApplyResult { get; set; }

        public WorkspaceQueryResult<CSharpCodeFixPreview>? FixAllPreviewResult { get; set; }

        public WorkspaceQueryResult<CSharpCodeFixApplyResult>? FixAllApplyResult { get; set; }

        public WorkspaceQueryResult<CSharpRefactoringPlan>? RefactoringPlanResult { get; set; }

        public int CallCount { get; private set; }

        public SymbolReferenceRequest? LastDefinitionRequest { get; private set; }

        public SymbolReferenceRequest? LastReferenceRequest { get; private set; }

        public SymbolReferenceRequest? LastImplementationRequest { get; private set; }

        public SymbolReferenceRequest? LastOverrideRequest { get; private set; }

        public SymbolDescriptionRequest? LastDescriptionRequest { get; private set; }

        public DiagnosticsRequest? LastDiagnosticsRequest { get; private set; }

        public ErrorListRequest? LastErrorListRequest { get; private set; }

        public OutputWindowRequest? LastOutputWindowRequest { get; private set; }

        public VisualStudioDocumentsRequest? LastOpenDocumentsRequest { get; private set; }

        public VisualStudioDocumentsRequest? LastActiveDocumentRequest { get; private set; }

        public SourceNavigationRequest? LastSourceNavigationRequest { get; private set; }

        public CallGraphRequest? LastCallersRequest { get; private set; }

        public CallGraphRequest? LastCalleesRequest { get; private set; }

        public SymbolImpactRequest? LastImpactRequest { get; private set; }

        public RelatedTestsRequest? LastRelatedTestsRequest { get; private set; }

        public SourceContextRequest? LastSymbolSourceRequest { get; private set; }

        public SourceContextRequest? LastSourceContextRequest { get; private set; }

        public TemporaryMarkersRequest? LastTemporaryMarkersRequest { get; private set; }

        public ProjectGraphRequest? LastProjectGraphRequest { get; private set; }

        public RenamePreviewRequest? LastRenamePreviewRequest { get; private set; }

        public RenameApplyRequest? LastRenameApplyRequest { get; private set; }

        public CSharpCodeFixListRequest? LastCodeFixListRequest { get; private set; }

        public CSharpCodeFixRequest? LastCodeFixRequest { get; private set; }

        public CSharpRefactoringPlanRequest? LastRefactoringPlanRequest { get; private set; }

        public DerivedTypesRequest? LastDerivedTypesRequest { get; private set; }

        public InheritanceChainRequest? LastInheritanceChainRequest { get; private set; }

        public WorkspaceStatusRequest? LastWorkspaceStatusRequest { get; private set; }

        public SymbolSearchRequest? LastSearchRequest { get; private set; }

        public DebugControlRequest? LastDebugControlRequest { get; private set; }

        public DebugBreakpointMutationRequest? LastBreakpointMutationRequest { get; private set; }

        public Task<WorkspaceQueryResult<VisualStudioBridgeInstanceDescriptor>> ListVisualStudioInstancesAsync(
            VisualStudioInstancesRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (InstancesResults is { Count: > 0 })
            {
                return Task.FromResult(InstancesResults.Dequeue());
            }

            return Task.FromResult(InstancesResult ?? Empty<VisualStudioBridgeInstanceDescriptor>());
        }

        public Task<WorkspaceQueryResult<WorkspaceStatus>> GetWorkspaceStatusAsync(
            WorkspaceStatusRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastWorkspaceStatusRequest = request;
            return Task.FromResult(WorkspaceStatusResult ?? Empty<WorkspaceStatus>());
        }

        public Task<WorkspaceQueryResult<SymbolDescriptor>> SearchSymbolsAsync(
            SymbolSearchRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSearchRequest = request;
            return Task.FromResult(SearchResult ?? Empty<SymbolDescriptor>());
        }

        public Task<WorkspaceQueryResult<SymbolReference>> FindReferencesAsync(
            SymbolReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastReferenceRequest = request;
            return Task.FromResult(ReferenceResult ?? Empty<SymbolReference>());
        }

        public Task<WorkspaceQueryResult<SymbolDescriptor>> FindDefinitionsAsync(
            SymbolReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDefinitionRequest = request;
            return Task.FromResult(DefinitionResult ?? Empty<SymbolDescriptor>());
        }

        public Task<WorkspaceQueryResult<SymbolReference>> FindImplementationsAsync(
            SymbolReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastImplementationRequest = request;
            return Task.FromResult(Empty<SymbolReference>());
        }

        public Task<WorkspaceQueryResult<SymbolReference>> FindOverridesAsync(
            SymbolReferenceRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastOverrideRequest = request;
            return Task.FromResult(Empty<SymbolReference>());
        }

        public Task<WorkspaceQueryResult<SymbolDescription>> DescribeSymbolAsync(
            SymbolDescriptionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDescriptionRequest = request;
            return Task.FromResult(Empty<SymbolDescription>());
        }

        public Task<WorkspaceQueryResult<SourceContextSnippet>> GetSymbolSourceAsync(
            SourceContextRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSymbolSourceRequest = request;
            if (SymbolSourceResults is { Count: > 0 })
            {
                return Task.FromResult(SymbolSourceResults.Dequeue());
            }

            return Task.FromResult(SymbolSourceResult ?? Empty<SourceContextSnippet>());
        }

        public Task<WorkspaceQueryResult<SourceContextSnippet>> GetSourceContextAsync(
            SourceContextRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSourceContextRequest = request;
            if (SourceContextResults is { Count: > 0 })
            {
                return Task.FromResult(SourceContextResults.Dequeue());
            }

            return Task.FromResult(SourceContextResult ?? Empty<SourceContextSnippet>());
        }

        public Task<WorkspaceQueryResult<DocumentSymbolNode>> ListDocumentSymbolsAsync(
            DocumentSymbolsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DocumentSymbolNode>());
        }

        public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetOpenDocumentsAsync(
            VisualStudioDocumentsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastOpenDocumentsRequest = request;
            return Task.FromResult(OpenDocumentsResult ?? Empty<VisualStudioDocumentSnapshot>());
        }

        public Task<WorkspaceQueryResult<VisualStudioDocumentSnapshot>> GetActiveDocumentContextAsync(
            VisualStudioDocumentsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastActiveDocumentRequest = request;
            return Task.FromResult(ActiveDocumentResult ?? Empty<VisualStudioDocumentSnapshot>());
        }

        public Task<WorkspaceQueryResult<SourceNavigationResult>> OpenSourceLocationAsync(
            SourceNavigationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastSourceNavigationRequest = request;
            return Task.FromResult(SourceNavigationResult ?? Empty<SourceNavigationResult>());
        }

        public Task<WorkspaceQueryResult<CodeDiagnostic>> GetDiagnosticsAsync(
            DiagnosticsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDiagnosticsRequest = request;
            return Task.FromResult(DiagnosticsResult ?? Empty<CodeDiagnostic>());
        }

        public Task<WorkspaceQueryResult<VisualStudioErrorListItem>> GetErrorListAsync(
            ErrorListRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastErrorListRequest = request;
            return Task.FromResult(Empty<VisualStudioErrorListItem>());
        }

        public Task<WorkspaceQueryResult<VisualStudioOutputWindowSnapshot>> GetOutputWindowAsync(
            OutputWindowRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastOutputWindowRequest = request;
            return Task.FromResult(OutputWindowResult ?? Empty<VisualStudioOutputWindowSnapshot>());
        }

        public Task<WorkspaceQueryResult<CallGraphEdge>> FindCallersAsync(
            CallGraphRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCallersRequest = request;
            return Task.FromResult(CallersResult ?? Empty<CallGraphEdge>());
        }

        public Task<WorkspaceQueryResult<CallGraphEdge>> FindCalleesAsync(
            CallGraphRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCalleesRequest = request;
            return Task.FromResult(CalleesResult ?? Empty<CallGraphEdge>());
        }

        public Task<WorkspaceQueryResult<SymbolImpactSummary>> AnalyzeSymbolImpactAsync(
            SymbolImpactRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastImpactRequest = request;
            return Task.FromResult(ImpactResult ?? Empty<SymbolImpactSummary>());
        }

        public Task<WorkspaceQueryResult<RelatedTestDescriptor>> FindRelatedTestsAsync(
            RelatedTestsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRelatedTestsRequest = request;
            return Task.FromResult(RelatedTestsResult ?? Empty<RelatedTestDescriptor>());
        }

        public Task<WorkspaceQueryResult<RenamePreview>> PreviewRenameAsync(
            RenamePreviewRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRenamePreviewRequest = request;
            return Task.FromResult(Empty<RenamePreview>());
        }

        public Task<WorkspaceQueryResult<RenameApplyResult>> ApplyRenameAsync(
            RenameApplyRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRenameApplyRequest = request;
            return Task.FromResult(Empty<RenameApplyResult>());
        }

        public Task<WorkspaceQueryResult<CSharpCleanupPreview>> PreviewCleanupAsync(
            CSharpCleanupRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<CSharpCleanupPreview>());
        }

        public Task<WorkspaceQueryResult<CSharpCleanupApplyResult>> ApplyCleanupAsync(
            CSharpCleanupApplyRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<CSharpCleanupApplyResult>());
        }

        public Task<WorkspaceQueryResult<CSharpCodeFixCandidate>> ListCodeFixesAsync(
            CSharpCodeFixListRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCodeFixListRequest = request;
            return Task.FromResult(Empty<CSharpCodeFixCandidate>());
        }

        public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewCodeFixAsync(
            CSharpCodeFixRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCodeFixRequest = request;
            return Task.FromResult(CodeFixPreviewResult ?? Empty<CSharpCodeFixPreview>());
        }

        public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyCodeFixAsync(
            CSharpCodeFixRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCodeFixRequest = request;
            return Task.FromResult(CodeFixApplyResult ?? new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Items = new[]
                {
                    new CSharpCodeFixApplyResult
                    {
                        Applied = true,
                    },
                },
            });
        }

        public Task<WorkspaceQueryResult<CSharpCodeFixPreview>> PreviewFixAllAsync(
            CSharpCodeFixRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCodeFixRequest = request;
            return Task.FromResult(FixAllPreviewResult ?? Empty<CSharpCodeFixPreview>());
        }

        public Task<WorkspaceQueryResult<CSharpCodeFixApplyResult>> ApplyFixAllAsync(
            CSharpCodeFixRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCodeFixRequest = request;
            return Task.FromResult(FixAllApplyResult ?? new WorkspaceQueryResult<CSharpCodeFixApplyResult>
            {
                Items = new[]
                {
                    new CSharpCodeFixApplyResult
                    {
                        Applied = true,
                    },
                },
            });
        }

        public Task<WorkspaceQueryResult<CSharpRefactoringPlan>> PreviewRefactoringPlanAsync(
            CSharpRefactoringPlanRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRefactoringPlanRequest = request;
            return Task.FromResult(RefactoringPlanResult ?? Empty<CSharpRefactoringPlan>());
        }

        public Task<WorkspaceQueryResult<DerivedTypeDescriptor>> FindDerivedTypesAsync(
            DerivedTypesRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDerivedTypesRequest = request;
            return Task.FromResult(Empty<DerivedTypeDescriptor>());
        }

        public Task<WorkspaceQueryResult<InheritanceChain>> GetInheritanceChainAsync(
            InheritanceChainRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastInheritanceChainRequest = request;
            return Task.FromResult(Empty<InheritanceChain>());
        }

        public Task<WorkspaceQueryResult<ProjectGraph>> GetProjectGraphAsync(
            ProjectGraphRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastProjectGraphRequest = request;
            return Task.FromResult(ProjectGraphResult ?? Empty<ProjectGraph>());
        }

        public Task<WorkspaceQueryResult<DebugSessionStatus>> GetDebuggerStatusAsync(
            DebuggerStatusRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugSessionStatus>());
        }

        public Task<WorkspaceQueryResult<DebugStackFrameInfo>> GetDebugCallStackAsync(
            DebugCallStackRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugStackFrameInfo>());
        }

        public Task<WorkspaceQueryResult<DebugVariableInfo>> GetDebugStackFrameVariablesAsync(
            DebugStackFrameVariablesRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugVariableInfo>());
        }

        public Task<WorkspaceQueryResult<DebugExpressionResult>> EvaluateDebugExpressionAsync(
            DebugExpressionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugExpressionResult>());
        }

        public Task<WorkspaceQueryResult<DebugThreadInfo>> ListDebugThreadsAsync(
            DebugThreadsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugThreadInfo>());
        }

        public Task<WorkspaceQueryResult<DebugBreakpointInfo>> ListDebugBreakpointsAsync(
            DebugBreakpointsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<DebugBreakpointInfo>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> StartDebuggingAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> ContinueDebuggingAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> BreakDebuggingAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> StopDebuggingAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> StepOverAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> StepIntoAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> StepOutAsync(
            DebugControlRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastDebugControlRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> SetDebugBreakpointAsync(
            DebugBreakpointMutationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBreakpointMutationRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> RemoveDebugBreakpointAsync(
            DebugBreakpointMutationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBreakpointMutationRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<DebugControlResult>> EnableDebugBreakpointAsync(
            DebugBreakpointMutationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBreakpointMutationRequest = request;
            return Task.FromResult(Empty<DebugControlResult>());
        }

        public Task<WorkspaceQueryResult<GeneratedDocumentDescriptor>> ListGeneratedDocumentsAsync(
            GeneratedDocumentsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<GeneratedDocumentDescriptor>());
        }

        public Task<WorkspaceQueryResult<TemporaryMarker>> FindTemporaryMarkersAsync(
            TemporaryMarkersRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastTemporaryMarkersRequest = request;
            return Task.FromResult(TemporaryMarkersResult ?? Empty<TemporaryMarker>());
        }

        public Task<WorkspaceQueryResult<EnclosingContext>> GetEnclosingContextAsync(
            EnclosingContextRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Empty<EnclosingContext>());
        }

        private static WorkspaceQueryResult<T> Empty<T>()
        {
            return new WorkspaceQueryResult<T>
            {
                Items = Array.Empty<T>(),
                Diagnostics = Array.Empty<string>(),
                IsPartial = false,
            };
        }
    }

    private sealed class RecordingSolutionLauncher : IVisualStudioSolutionLauncher
    {
        public string? DevenvPath { get; set; } = @"C:\VS\devenv.exe";

        public int ProcessId { get; set; } = 1234;

        public string[] Diagnostics { get; set; } = Array.Empty<string>();

        public int LaunchCount { get; private set; }

        public string? LastDevenvPath { get; private set; }

        public string? LastSolutionPath { get; private set; }

        public string? FindDevenvPath()
        {
            return DevenvPath;
        }

        public VisualStudioLaunchResult LaunchSolution(string devenvPath, string solutionPath)
        {
            LaunchCount++;
            LastDevenvPath = devenvPath;
            LastSolutionPath = solutionPath;
            return new VisualStudioLaunchResult
            {
                ProcessId = ProcessId,
                Diagnostics = Diagnostics,
            };
        }
    }

    private sealed class RecordingActivityLogReader : IVisualStudioActivityLogReader
    {
        private readonly string[] _diagnostics;

        public RecordingActivityLogReader(params string[] diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public int ReadCount { get; private set; }

        public string[] ReadRecentIssues(int maxIssues = 5)
        {
            ReadCount++;
            return _diagnostics;
        }
    }

    private static WorkspaceQueryResult<T> QueryResult<T>(params T[] items)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = items,
            Diagnostics = Array.Empty<string>(),
            IsPartial = false,
        };
    }

    private static WorkspaceQueryResult<T> PartialQueryResult<T>(params string[] diagnostics)
    {
        return new WorkspaceQueryResult<T>
        {
            Items = Array.Empty<T>(),
            Diagnostics = diagnostics,
            IsPartial = true,
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodeNavigatorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
