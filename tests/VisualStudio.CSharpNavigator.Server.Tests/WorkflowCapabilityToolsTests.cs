using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class WorkflowCapabilityToolsTests
{
    [Fact]
    public void DefaultDiscoveryMatchesSdkAssemblyRegistrationWithoutBridgeServices()
    {
        // 不注册桥接或启动宿主，仅解析 SDK 注册的工具元数据。
        var services = new ServiceCollection();
        services.AddMcpServer().WithToolsFromAssembly(typeof(WorkflowCapabilityTools).Assembly);
        using var provider = services.BuildServiceProvider();
        var registeredNames = provider.GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var result = new WorkflowCapabilityTools().GetCSharpWorkflowCapabilities();
        var report = Assert.Single(result.Items);
        var reportedNames = report.Capabilities.SelectMany(capability => capability.AvailableTools)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.False(result.IsPartial);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(registeredNames, reportedNames);
        Assert.Equal(registeredNames.Length, registeredNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(registeredNames.Length, report.RegisteredToolCount);
        Assert.Equal(registeredNames.Length, report.ReturnedToolCount);
        Assert.Contains("get_csharp_workflow_capabilities", reportedNames);
        Assert.All(report.Capabilities, capability =>
        {
            Assert.Contains(capability.TaskCategory, report.SupportedTaskCategories);
            Assert.All(capability.EntryPoints, name => Assert.Contains(name, capability.AvailableTools));
        });
    }

    [Theory]
    [InlineData("edit", "prepare_csharp_edit_task")]
    [InlineData("review", "prepare_csharp_change_review")]
    [InlineData("verification", "prepare_csharp_verification_run")]
    [InlineData("build", "investigate_csharp_build_failure")]
    [InlineData("diagnostics", "get_csharp_diagnostics")]
    [InlineData("mutation", "preview_csharp_refactoring_plan")]
    [InlineData("debug", "investigate_csharp_runtime_exception")]
    [InlineData("workspace", "prepare_csharp_workspace")]
    public void CategoryFilterReturnsOnlyTheRequestedRoute(string category, string firstEntryPoint)
    {
        var report = GetReport(category);
        var capability = Assert.Single(report.Capabilities);

        Assert.Equal(category, report.TaskCategory);
        Assert.Equal(category, capability.TaskCategory);
        Assert.Equal(firstEntryPoint, capability.EntryPoints[0]);
        Assert.Equal(capability.AvailableTools.Length, report.ReturnedToolCount);
        Assert.True(report.RegisteredToolCount > report.ReturnedToolCount);
        Assert.DoesNotContain("get_csharp_workflow_performance_snapshot", capability.AvailableTools);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" ALL ")]
    public void EmptyOrAllFilterReturnsTheWholeCatalog(string? category)
    {
        var report = GetReport(category);

        Assert.Equal("all", report.TaskCategory);
        Assert.Equal(report.RegisteredToolCount, report.ReturnedToolCount);
    }

    [Fact]
    public void FilterIgnoresCaseAndSurroundingWhitespace()
    {
        var report = GetReport("  EdIt  ");

        Assert.Equal("edit", Assert.Single(report.Capabilities).TaskCategory);
    }

    [Fact]
    public void UnknownCategoryIsNotReportedAsAnEmptySuccess()
    {
        var result = new WorkflowCapabilityTools().GetCSharpWorkflowCapabilities("not-a-category");

        Assert.True(result.IsPartial);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("UnknownWorkflowTaskCategory:", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscoveryNeverClaimsVsixVerificationOrPermissionToRetryMutations()
    {
        var report = GetReport();

        Assert.True(report.IsLocalOnly);
        Assert.Equal("ServerAssemblyRegistration", report.DiscoveryScope);
        Assert.NotEmpty(report.ServerAssemblyVersion);
        Assert.Equal("NotChecked", report.Safety.VsixVerificationStatus);
        Assert.Equal("NotVerified", report.Safety.VersionSafetyStatus);
        Assert.False(report.Safety.BridgeCapabilitiesVerified);
        Assert.False(report.Safety.AutomaticMutationRetryAllowed);
        Assert.NotEmpty(report.Safety.Notes);
    }

    [Fact]
    public void OperationStatusRouteIsAdvertisedOnlyWhenTheToolIsRegistered()
    {
        const string toolName = "get_csharp_operation_status";
        var services = new ServiceCollection();
        services.AddMcpServer().WithToolsFromAssembly(typeof(WorkflowCapabilityTools).Assembly);
        using var provider = services.BuildServiceProvider();
        var isRegistered = provider.GetServices<McpServerTool>().Any(tool => tool.ProtocolTool.Name == toolName);
        var result = new WorkflowCapabilityTools().GetCSharpWorkflowCapabilities("operation-status");
        var report = Assert.Single(result.Items);

        Assert.Equal(isRegistered ? toolName : string.Empty, report.Safety.OperationStatusTool);
        Assert.Equal(isRegistered, report.Safety.OperationStatusRequiresExplicitTarget);
        Assert.Equal(isRegistered, report.Safety.OperationStatusSupportsRecentListing);
        Assert.False(report.Safety.AutomaticMutationRetryAllowed);
        Assert.False(report.Safety.BridgeCapabilitiesVerified);
        Assert.False(result.IsPartial);
        if (isRegistered)
        {
            var capability = Assert.Single(report.Capabilities);
            Assert.Equal(toolName, Assert.Single(capability.EntryPoints));
            Assert.Equal(toolName, Assert.Single(capability.AvailableTools));
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            Assert.Empty(report.Capabilities);
            Assert.Equal(0, report.ReturnedToolCount);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith("NoRegisteredToolsForTaskCategory:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ReturnedArraysCannotChangeLaterDiscoveryResults()
    {
        var report = GetReport("edit");
        var capability = Assert.Single(report.Capabilities);
        capability.EntryPoints[0] = "not-registered";
        capability.AvailableTools[0] = "not-registered";
        report.SupportedTaskCategories[0] = "not-registered";
        report.Safety.Notes[0] = "not-registered";

        var next = GetReport("edit");
        var nextCapability = Assert.Single(next.Capabilities);
        Assert.Equal("prepare_csharp_edit_task", nextCapability.EntryPoints[0]);
        Assert.DoesNotContain("not-registered", nextCapability.AvailableTools);
        Assert.DoesNotContain("not-registered", next.SupportedTaskCategories);
        Assert.DoesNotContain("not-registered", next.Safety.Notes);
    }

    [Fact]
    public void CancelledDiscoveryStopsBeforeReturningACatalog()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new WorkflowCapabilityTools().GetCSharpWorkflowCapabilities(cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ToolHasReadOnlyLocalAnnotationsAndNoInjectedBridgeDependency()
    {
        var method = typeof(WorkflowCapabilityTools).GetMethod(nameof(WorkflowCapabilityTools.GetCSharpWorkflowCapabilities))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

        Assert.Equal("get_csharp_workflow_capabilities", attribute.Name);
        Assert.True(attribute.ReadOnly);
        Assert.True(attribute.Idempotent);
        Assert.False(attribute.OpenWorld);
        Assert.Empty(Assert.Single(typeof(WorkflowCapabilityTools).GetConstructors()).GetParameters());
        Assert.True(method.GetParameters().Single(parameter => parameter.Name == "taskCategory").HasDefaultValue);
    }

    private static CSharpWorkflowCapabilities GetReport(string? category = null) =>
        Assert.Single(new WorkflowCapabilityTools().GetCSharpWorkflowCapabilities(category).Items);
}
