using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed partial class CodeNavigationToolsTests
{
    [Fact]
    public async Task AgenticWorkflowDeadlineReturnsStablePartialResult()
    {
        var bridge = new RecordingBridge
        {
            ListInstancesDelayMilliseconds = 1500,
        };

        var result = await CreateWorkflowKernel(bridge).PrepareCSharpEditTaskAsync(
            new CSharpEditTaskRequest
            {
                MaxElapsedMilliseconds = 1000,
            },
            CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("WorkflowDeadlineExceeded:", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("WorkflowDeadlineResultUnavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DebugScenarioExecutionRequiresExplicitTarget()
    {
        var tools = new DebugScenarioExecutionTools(new CodeNavigationTools(new RecordingBridge()));

        var result = await tools.ExecuteCSharpDebugScenario(
            scenarioName: "save-crash",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("DebugScenarioTargetRequired:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DebugScenarioExecutionLookupReportsUnknownOwnership()
    {
        var tools = new DebugScenarioExecutionTools(new CodeNavigationTools(new RecordingBridge()));

        var result = await tools.GetCSharpDebugScenarioExecution(
            "missing-execution",
            CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("DebugScenarioExecutionNotFoundOrExpired:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerificationExecutionRejectsShellLikeInvalidInputBeforeStartingProcess()
    {
        var tools = new VerificationExecutionTools();

        var result = await tools.ExecuteCSharpVerification(
            targetPath: Path.Combine(Path.GetTempPath(), "missing.sln"),
            kind: CSharpVerificationCommandKind.Build,
            testFilter: "Name~ShouldNotBeAccepted",
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("TargetPath must point to an existing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchCache_ReusesOnlyTheSameSnapshot()
    {
        var status = new WorkspaceStatus { WorkspaceVersion = "snapshot:one", IsSolutionLoaded = true };
        var bridge = new RecordingBridge
        {
            WorkspaceStatusResult = QueryResult(status),
            SearchResult = QueryResult(new SymbolDescriptor { Name = "Before" }),
        };
        // Disable the status memoization so a version change is observed
        // immediately, which is what this test exercises.
        var tools = new CodeNavigationTools(bridge, statusCacheTtl: TimeSpan.Zero);
        Assert.Equal("Before", Assert.Single((await tools.SearchCSharpSymbols("A")).Items).Name);
        await tools.SearchCSharpSymbols("A");
        Assert.Equal(1, bridge.CallCount);
        status.WorkspaceVersion = "snapshot:two";
        bridge.SearchResult = QueryResult(new SymbolDescriptor { Name = "After" });
        Assert.Equal("After", Assert.Single((await tools.SearchCSharpSymbols("A")).Items).Name);
        Assert.Equal(2, bridge.CallCount);
        Assert.Equal(3, bridge.SnapshotCallCount);
    }

    [Fact]
    public async Task SearchCache_OldBridgeAndPartialResultsAreNotCached()
    {
        var status = new WorkspaceStatus { IsSolutionLoaded = true };
        var bridge = new RecordingBridge { WorkspaceStatusResult = QueryResult(status) };
        var tools = new CodeNavigationTools(bridge, statusCacheTtl: TimeSpan.Zero);
        await tools.SearchCSharpSymbols("A");
        await tools.SearchCSharpSymbols("A");
        Assert.Equal(2, bridge.CallCount);
        status.WorkspaceVersion = "snapshot:one";
        bridge.SearchResult = new WorkspaceQueryResult<SymbolDescriptor> { IsPartial = true };
        await tools.SearchCSharpSymbols("A");
        await tools.SearchCSharpSymbols("A");
        Assert.Equal(4, bridge.CallCount);
        Assert.Equal(4, bridge.SnapshotCallCount);
    }

    [Fact]
    public async Task SearchCache_RejectsAChangedSolutionBeforeQuerying()
    {
        var bridge = new RecordingBridge
        {
            WorkspaceStatusResult = QueryResult(new WorkspaceStatus
            {
                IsSolutionLoaded = true, InstanceId = "vs", SolutionPath = @"D:\B.sln", WorkspaceVersion = "snapshot:two",
            }),
        };
        var result = await new CodeNavigationTools(bridge).SearchCSharpSymbols("A",
            targetInstanceId: "vs", targetSolutionPath: @"D:\A.sln");
        Assert.Empty(result.Items);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, message => message.StartsWith("WorkspaceTargetMismatch:"));
        Assert.Equal(0, bridge.CallCount);
        Assert.Equal(1, bridge.SnapshotCallCount);
    }

    [Fact]
    public async Task DiagnosticsRelevanceDoesNotClaimThatAnErrorIsNew()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(new VisualStudioBridgeInstanceDescriptor
            {
                InstanceId = "vs", PipeName = "pipe", SolutionPath = @"D:\A.sln", IsAlive = true,
            }),
            WorkspaceStatusResult = QueryResult(new WorkspaceStatus
            {
                IsSolutionLoaded = true, InstanceId = "vs", SolutionPath = @"D:\A.sln",
            }),
            DiagnosticsResult = QueryResult(new CodeDiagnostic
            {
                Id = "CS0103", Message = "Unknown name", Severity = CodeDiagnosticSeverity.Error,
                ScopeReasons = new[] { "changed file" }, Span = new SourceSpan { FilePath = @"D:\A.cs", StartLine = 1 },
            }),
        };
        var result = await new CodeNavigationTools(bridge).GetCSharpTaskContext(
            buildOutput: @"D:\A.cs(1,1): error CS0103: Unknown name",
            changedFiles: new[] { @"D:\A.cs" }, maxSymbols: 0, maxSourceSnippets: 0, maxRelatedItems: 0);
        var context = Assert.Single(result.Items);
        Assert.Contains(context.PrimaryDiagnostics, diagnostic => diagnostic.Source == "build");
        Assert.Contains(context.PrimaryDiagnostics, diagnostic => diagnostic.Source == "diagnostic");
        Assert.All(context.PrimaryDiagnostics, diagnostic => Assert.Equal(DiagnosticBaselineKind.Unknown, diagnostic.BaselineKind));
    }

    [Fact]
    public async Task LeaseConflictIsRejectedBeforeAnyWorkspaceQuery()
    {
        var bridge = new RecordingBridge();
        var leases = new WorkspaceContextLeaseStore();
        var lease = leases.Create(new VisualStudioBridgeTarget { InstanceId = "vs-a", SolutionPath = @"D:\A.sln" }, @"D:\A.sln");
        var kernel = CreateWorkflowKernel(bridge, leases: leases);
        var result = await kernel.PrepareCSharpEditTaskAsync(new CSharpEditTaskRequest
        {
            WorkspaceContextLeaseId = lease.LeaseId,
            Target = new VisualStudioBridgeTarget { InstanceId = "vs-b" },
        }, CancellationToken.None);
        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, message => message.StartsWith("WorkspaceContextLeaseTargetMismatch:"));
        Assert.Equal(0, bridge.CallCount);
        Assert.Equal(0, bridge.SnapshotCallCount);
    }

    [Fact]
    public async Task LeaseCannotBeReusedAfterTheSameVsOpensAnotherSolution()
    {
        var bridge = new RecordingBridge
        {
            InstancesResult = QueryResult(new VisualStudioBridgeInstanceDescriptor
            {
                InstanceId = "vs", PipeName = "pipe", SolutionPath = @"D:\B.sln", IsAlive = true,
            }),
        };
        var leases = new WorkspaceContextLeaseStore();
        var lease = leases.Create(new VisualStudioBridgeTarget
        {
            InstanceId = "vs", PipeName = "pipe", SolutionPath = @"D:\A.sln",
        }, @"D:\A.sln");
        var result = await CreateWorkflowKernel(bridge, leases: leases).PrepareCSharpEditTaskAsync(
            new CSharpEditTaskRequest { WorkspaceContextLeaseId = lease.LeaseId }, CancellationToken.None);
        Assert.Empty(result.Items);
        Assert.True(result.IsPartial);
        Assert.Contains(result.Diagnostics, message => message.StartsWith("WorkspaceContextLeaseInvalidated:"));
        Assert.Equal(1, bridge.CallCount);
        Assert.Equal(0, bridge.SnapshotCallCount);
    }

    [Fact]
    public void ReturnedLeaseCannotModifyTheStoredTarget()
    {
        var store = new WorkspaceContextLeaseStore();
        var lease = store.Create(new VisualStudioBridgeTarget { InstanceId = "vs" }, @"D:\A.sln");
        lease.Target.InstanceId = "other";
        Assert.True(store.TryGet(lease.LeaseId, out var stored));
        Assert.Equal("vs", stored.Target.InstanceId);
        stored.Target.InstanceId = "third";
        Assert.True(store.TryGet(lease.LeaseId, out var again));
        Assert.Equal("vs", again.Target.InstanceId);
    }
}
