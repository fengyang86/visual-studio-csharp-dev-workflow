using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class WorkflowCapabilityStdioTests
{
    [Fact]
    public async Task RealServerPublishesCapabilitiesAndRejectsUntargetedRecoveryWithoutVs()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "workflow-capability-test",
            Command = "dotnet",
            Arguments = new[] { Path.Combine(AppContext.BaseDirectory, "VisualStudio.CSharpNavigator.Server.dll") },
            WorkingDirectory = AppContext.BaseDirectory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["VisualStudioBridge__DiscoveryDirectory"] = Path.Combine(Path.GetTempPath(), "absent-bridges-" + Guid.NewGuid().ToString("N")),
            },
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, tool => tool.Name == "get_csharp_operation_status");
        var response = await client.CallToolAsync("get_csharp_workflow_capabilities",
            new Dictionary<string, object?> { ["taskCategory"] = "edit" }, cancellationToken: timeout.Token);
        var result = Deserialize<CSharpWorkflowCapabilities>(response);
        Assert.False(result.IsPartial);
        var report = Assert.Single(result.Items);
        Assert.Equal(tools.Count, report.RegisteredToolCount);
        Assert.Equal("prepare_csharp_edit_task", Assert.Single(report.Capabilities).EntryPoints[0]);
        Assert.False(report.Safety.BridgeCapabilitiesVerified);

        var rejected = Deserialize<BridgeOperationStatus>(await client.CallToolAsync("get_csharp_operation_status",
            new Dictionary<string, object?>(), cancellationToken: timeout.Token));
        Assert.True(rejected.IsPartial);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.StartsWith("ExplicitTargetRequired:", StringComparison.Ordinal));
    }

    private static WorkspaceQueryResult<T> Deserialize<T>(CallToolResult result)
    {
        Assert.False(result.IsError ?? false);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        return JsonSerializer.Deserialize<WorkspaceQueryResult<T>>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
