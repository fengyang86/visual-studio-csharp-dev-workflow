using System.IO.Pipes;
using System.Text;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Bridge;
using Microsoft.Extensions.Options;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class NamedPipeVisualStudioWorkspaceBridgeTests
{
    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenPipeIsMissing_ReturnsExplicitDiagnostic()
    {
        var bridge = CreateBridge("VisualStudio.CSharpNavigator.Tests.Missing." + Guid.NewGuid(), 1);

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Visual Studio bridge pipe"));
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_SendsTypedRequestAndReadsTypedResult()
    {
        var pipeName = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        var serverTask = RunWorkspaceStatusResponseServerAsync(pipeName);
        var bridge = CreateBridge(pipeName, 5000);

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var status = Assert.Single(result.Items);
        Assert.False(result.IsPartial);
        Assert.True(status.IsSolutionLoaded);
        Assert.Equal(@"D:\Samples\SampleWorkspace\SampleWorkspace.sln", status.SolutionPath);
        Assert.Equal("SampleWorkspace", status.SolutionName);
        Assert.Equal(123, status.ProjectCount);
        Assert.Equal(4567, status.DocumentCount);
        Assert.Equal("Debug", status.ActiveConfigurationName);
        Assert.Equal("Any CPU", status.ActivePlatformName);
        Assert.Equal(new[] { "SampleWorkspace.App" }, status.StartupProjects);
        var project = Assert.Single(status.Projects);
        Assert.Equal("SampleWorkspace.Core", project.ProjectName);
        Assert.Equal(@"D:\Samples\SampleWorkspace\src\SampleWorkspace.Core\SampleWorkspace.Core.csproj", project.FilePath);
        Assert.Equal("C#", project.Language);
        Assert.Equal(new[] { "net8.0" }, project.TargetFrameworks);
    }

    [Fact]
    public async Task SearchSymbolsAsync_PreservesSymbolKeyAcrossJsonBridge()
    {
        var pipeName = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        var serverTask = RunSymbolSearchResponseServerAsync(pipeName);
        var bridge = CreateBridge(pipeName, 5000);

        var result = await bridge.SearchSymbolsAsync(
            new SymbolSearchRequest { QueryText = "SetProps", MaxResults = 10 },
            CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var symbol = Assert.Single(result.Items);
        Assert.Equal("M:SampleWorkspace.Sample.SetProps", symbol.Key?.Value);
        Assert.Equal("SetProps", symbol.Name);
        Assert.Equal(CodeSymbolKind.Method, symbol.Kind);
        Assert.Equal(42, symbol.Span?.StartLine);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenMultipleInstancesMatch_ReturnsSelectionDiagnostic()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(discovery.Path, "vs-a", Environment.ProcessId, @"D:\WorkCodes\A\A.sln", "pipe-a");
        WriteInstance(discovery.Path, "vs-b", Environment.ProcessId, @"D:\WorkCodes\B\B.sln", "pipe-b");

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            ConnectTimeoutMilliseconds = 1,
        });

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Multiple Visual Studio bridge instances"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("vs-a"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("vs-b"));
    }

    [Fact]
    public async Task ListVisualStudioInstancesAsync_PreservesBridgeVersionMetadata()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(
            discovery.Path,
            "vs-versioned",
            Environment.ProcessId,
            @"D:\WorkCodes\A\A.sln",
            "pipe-a",
            bridgeProtocolVersion: "1",
            extensionAssemblyVersion: "1.2.3.4",
            extensionFileVersion: "1.2.3");
        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            ConnectTimeoutMilliseconds = 1,
        });

        var result = await bridge.ListVisualStudioInstancesAsync(new VisualStudioInstancesRequest(), CancellationToken.None);

        var instance = Assert.Single(result.Items);
        Assert.Equal("1", instance.BridgeProtocolVersion);
        Assert.Equal("1.2.3.4", instance.ExtensionAssemblyVersion);
        Assert.Equal("1.2.3", instance.ExtensionFileVersion);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenSameSolutionPathMatchesMultipleInstances_AsksForInstanceOrPipe()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(discovery.Path, "vs-a", Environment.ProcessId, @"D:\WorkCodes\A\A.sln", "pipe-a");
        WriteInstance(discovery.Path, "vs-b", Environment.ProcessId, @"D:\WorkCodes\A\A.sln", "pipe-b");

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            ConnectTimeoutMilliseconds = 1,
        });

        var result = await bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest
            {
                Target = new VisualStudioBridgeTarget
                {
                    SolutionPath = @"D:\WorkCodes\A\A.sln",
                },
            },
            CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("targetInstanceId"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("targetPipeName"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Contains("targetSolutionPath to disambiguate"));
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenSolutionPathIsConfigured_SelectsMatchingInstance()
    {
        using var discovery = new TemporaryDirectory();
        var selectedPipe = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        WriteInstance(discovery.Path, "vs-a", Environment.ProcessId, @"D:\WorkCodes\A\A.sln", "pipe-a");
        WriteInstance(discovery.Path, "vs-realproject", Environment.ProcessId, @"D:\Samples\SampleWorkspace\SampleWorkspace.sln", selectedPipe);
        var serverTask = RunWorkspaceStatusResponseServerAsync(selectedPipe);

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            SolutionPath = @"D:\Samples\SampleWorkspace\SampleWorkspace.sln",
            ConnectTimeoutMilliseconds = 5000,
        });

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var status = Assert.Single(result.Items);
        Assert.False(result.IsPartial);
        Assert.Equal(@"D:\Samples\SampleWorkspace\SampleWorkspace.sln", status.SolutionPath);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenTargetSolutionPathIsProvided_OverridesGlobalPipeName()
    {
        using var discovery = new TemporaryDirectory();
        var selectedPipe = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        WriteInstance(discovery.Path, "vs-target", Environment.ProcessId, @"D:\WorkCodes\Target\Target.sln", selectedPipe);
        var serverTask = RunWorkspaceStatusResponseServerAsync(selectedPipe);

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            PipeName = "VisualStudio.CSharpNavigator.Tests.Missing." + Guid.NewGuid(),
            ConnectTimeoutMilliseconds = 5000,
        });

        var result = await bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest
            {
                Target = new VisualStudioBridgeTarget
                {
                    SolutionPath = @"D:\WorkCodes\Target\Target.sln",
                },
            },
            CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var status = Assert.Single(result.Items);
        Assert.False(result.IsPartial);
        Assert.True(status.IsSolutionLoaded);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenTargetInstanceIdIsProvided_OverridesGlobalPipeName()
    {
        using var discovery = new TemporaryDirectory();
        var selectedPipe = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        WriteInstance(discovery.Path, "vs-target", Environment.ProcessId, @"D:\WorkCodes\Target\Target.sln", selectedPipe);
        var serverTask = RunWorkspaceStatusResponseServerAsync(selectedPipe);

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            PipeName = "VisualStudio.CSharpNavigator.Tests.Missing." + Guid.NewGuid(),
            ConnectTimeoutMilliseconds = 5000,
        });

        var result = await bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest
            {
                Target = new VisualStudioBridgeTarget
                {
                    InstanceId = "vs-target",
                },
            },
            CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var status = Assert.Single(result.Items);
        Assert.False(result.IsPartial);
        Assert.True(status.IsSolutionLoaded);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenTargetMatchesDespiteLockedUnrelatedDiscoveryFile_Succeeds()
    {
        using var discovery = new TemporaryDirectory();
        var selectedPipe = "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid();
        WriteInstance(discovery.Path, "vs-target", Environment.ProcessId, @"D:\WorkCodes\Target\Target.sln", selectedPipe);
        var serverTask = RunWorkspaceStatusResponseServerAsync(selectedPipe);

        var lockedFilePath = Path.Combine(discovery.Path, "locked.json");
        await using (var lockedStream = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        await using (var writer = new StreamWriter(lockedStream, Encoding.UTF8, leaveOpen: true))
        {
            await writer.WriteAsync("{\"kind\":\"locked\"}");
            await writer.FlushAsync();
        }

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            ConnectTimeoutMilliseconds = 5000,
        });

        var result = await bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest
            {
                Target = new VisualStudioBridgeTarget
                {
                    SolutionPath = @"D:\WorkCodes\Target\Target.sln",
                },
            },
            CancellationToken.None);

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        var status = Assert.Single(result.Items);
        Assert.False(result.IsPartial);
        Assert.True(status.IsSolutionLoaded);
        Assert.Equal(@"D:\Samples\SampleWorkspace\SampleWorkspace.sln", status.SolutionPath);
    }

    [Fact]
    public async Task ListVisualStudioInstancesAsync_WhenIncludeStaleTrue_ReturnsActiveAndStaleDescriptors()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(discovery.Path, "vs-active", Environment.ProcessId, @"D:\WorkCodes\Active\Active.sln", "pipe-active");
        WriteInstance(
            discovery.Path,
            "vs-stale",
            processId: -1,
            solutionPath: @"D:\WorkCodes\Stale\Stale.sln",
            pipeName: "pipe-stale",
            lastSeenUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            DiscoveryStaleAfterSeconds = 30,
        });

        var result = await bridge.ListVisualStudioInstancesAsync(
            new VisualStudioInstancesRequest { IncludeStale = true },
            CancellationToken.None);

        Assert.False(result.IsPartial);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item => item.InstanceId == "vs-active" && !item.IsStale && item.IsAlive);
        Assert.Contains(result.Items, item => item.InstanceId == "vs-stale" && item.IsStale && !item.IsAlive);
    }

    [Fact]
    public async Task ListVisualStudioInstancesAsync_WhenIncludeStaleFalse_FiltersStaleDescriptors()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(discovery.Path, "vs-active", Environment.ProcessId, @"D:\WorkCodes\Active\Active.sln", "pipe-active");
        WriteInstance(
            discovery.Path,
            "vs-stale",
            processId: -1,
            solutionPath: @"D:\WorkCodes\Stale\Stale.sln",
            pipeName: "pipe-stale",
            lastSeenUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            DiscoveryStaleAfterSeconds = 30,
        });

        var result = await bridge.ListVisualStudioInstancesAsync(
            new VisualStudioInstancesRequest { IncludeStale = false },
            CancellationToken.None);

        Assert.False(result.IsPartial);
        var item = Assert.Single(result.Items);
        Assert.Equal("vs-active", item.InstanceId);
        Assert.False(item.IsStale);
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenTargetPipeNameMatchesOnlyStaleDiscovery_FailsBeforePipeTimeout()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(
            discovery.Path,
            "vs-stale",
            processId: -1,
            solutionPath: @"D:\WorkCodes\Stale\Stale.sln",
            pipeName: "pipe-stale",
            lastSeenUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            DiscoveryStaleAfterSeconds = 30,
            ConnectTimeoutMilliseconds = 5000,
        });

        var startedAt = DateTimeOffset.UtcNow;
        var result = await bridge.GetWorkspaceStatusAsync(
            new WorkspaceStatusRequest
            {
                Target = new VisualStudioBridgeTarget
                {
                    PipeName = "pipe-stale",
                },
            },
            CancellationToken.None);

        Assert.True(DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(1));
        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("stale discovery records"));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Process -1 is not running"));
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenOnlyStaleDiscoveryExists_DoesNotUseStaleDefaultTarget()
    {
        using var discovery = new TemporaryDirectory();
        WriteInstance(
            discovery.Path,
            "vs-stale",
            processId: -1,
            solutionPath: @"D:\WorkCodes\Stale\Stale.sln",
            pipeName: "pipe-stale",
            lastSeenUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            DiscoveryStaleAfterSeconds = 30,
            ConnectTimeoutMilliseconds = 1,
        });

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("No active Visual Studio bridge instance"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Contains("pipe-stale"));
    }

    [Fact]
    public async Task GetWorkspaceStatusAsync_WhenDiscoveryFileIsMalformed_ReturnsDiagnostic()
    {
        using var discovery = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(discovery.Path, "broken.json"), "{ not valid json");

        var bridge = CreateBridge(new NamedPipeBridgeOptions
        {
            DiscoveryDirectory = discovery.Path,
            ConnectTimeoutMilliseconds = 1,
        });

        var result = await bridge.GetWorkspaceStatusAsync(new WorkspaceStatusRequest(), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Invalid Visual Studio bridge discovery file"));
    }

    private static NamedPipeVisualStudioWorkspaceBridge CreateBridge(string pipeName, int timeoutMilliseconds)
    {
        return CreateBridge(new NamedPipeBridgeOptions
        {
            PipeName = pipeName,
            ConnectTimeoutMilliseconds = timeoutMilliseconds,
        });
    }

    private static NamedPipeVisualStudioWorkspaceBridge CreateBridge(NamedPipeBridgeOptions options)
    {
        return new NamedPipeVisualStudioWorkspaceBridge(Options.Create(options));
    }

    private static void WriteInstance(
        string discoveryDirectory,
        string instanceId,
        int processId,
        string solutionPath,
        string pipeName,
        string bridgeProtocolVersion = "",
        string extensionAssemblyVersion = "",
        string extensionFileVersion = "",
        DateTimeOffset? lastSeenUtc = null)
    {
        var instance = new VisualStudioBridgeInstance
        {
            InstanceId = instanceId,
            ProcessId = processId,
            SolutionPath = solutionPath,
            SolutionName = Path.GetFileNameWithoutExtension(solutionPath),
            PipeName = pipeName,
            BridgeProtocolVersion = bridgeProtocolVersion,
            ExtensionAssemblyVersion = extensionAssemblyVersion,
            ExtensionFileVersion = extensionFileVersion,
            LastSeenUtc = lastSeenUtc ?? DateTimeOffset.UtcNow,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(instance, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        File.WriteAllText(Path.Combine(discoveryDirectory, instanceId + ".json"), json);
    }

    private static async Task RunWorkspaceStatusResponseServerAsync(string pipeName)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync();

        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        var requestJson = await reader.ReadLineAsync();
        Assert.NotNull(requestJson);
        Assert.Contains("\"method\":\"GetWorkspaceStatus\"", requestJson);
        Assert.Contains("\"protocolVersion\":\"1\"", requestJson);

        const string responseJson =
            """
            {"protocolVersion":"1","result":{"items":[{"isSolutionLoaded":true,"solutionPath":"D:\\Samples\\SampleWorkspace\\SampleWorkspace.sln","solutionName":"SampleWorkspace","projectCount":123,"documentCount":4567,"activeConfigurationName":"Debug","activePlatformName":"Any CPU","startupProjects":["SampleWorkspace.App"],"projects":[{"projectName":"SampleWorkspace.Core","filePath":"D:\\Samples\\SampleWorkspace\\src\\SampleWorkspace.Core\\SampleWorkspace.Core.csproj","language":"C#","targetFrameworks":["net8.0"]}],"isProjectListPartial":false}],"diagnostics":[],"isPartial":false}}
            """;
        await writer.WriteLineAsync(responseJson);
    }

    private static async Task RunSymbolSearchResponseServerAsync(string pipeName)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync();

        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        var requestJson = await reader.ReadLineAsync();
        Assert.NotNull(requestJson);
        Assert.Contains("\"method\":\"SearchSymbols\"", requestJson);
        Assert.Contains("\"queryText\":\"SetProps\"", requestJson);

        const string responseJson =
            """
            {"protocolVersion":"1","result":{"items":[{"key":{"value":"M:SampleWorkspace.Sample.SetProps"},"name":"SetProps","containingType":"Sample","containingNamespace":"SampleWorkspace","projectName":"SampleWorkspace.Sample","kind":3,"span":{"filePath":"D:\\Samples\\SampleWorkspace\\Sample.cs","startLine":42,"startColumn":9,"endLine":42,"endColumn":17}}],"diagnostics":[],"isPartial":false}}
            """;
        await writer.WriteLineAsync(responseJson);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VisualStudio.CSharpNavigator.Tests." + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
