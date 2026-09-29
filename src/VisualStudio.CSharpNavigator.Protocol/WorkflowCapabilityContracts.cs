using System;

namespace VisualStudio.CSharpNavigator.Protocol;

public sealed class CSharpWorkflowCapabilities
{
    public string DiscoveryScope { get; set; } = string.Empty;

    public bool IsLocalOnly { get; set; }

    public string ServerAssemblyVersion { get; set; } = string.Empty;

    public string TaskCategory { get; set; } = string.Empty;

    public int RegisteredToolCount { get; set; }

    public int ReturnedToolCount { get; set; }

    public string[] SupportedTaskCategories { get; set; } = Array.Empty<string>();

    public CSharpWorkflowTaskCapability[] Capabilities { get; set; } = Array.Empty<CSharpWorkflowTaskCapability>();

    public CSharpWorkflowCapabilitySafety Safety { get; set; } = new CSharpWorkflowCapabilitySafety();
}

public sealed class CSharpWorkflowTaskCapability
{
    public string TaskCategory { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // 仅包含服务器已注册入口，按路由优先级排序；不保证目标 VSIX 支持。
    public string[] EntryPoints { get; set; } = Array.Empty<string>();

    public string[] AvailableTools { get; set; } = Array.Empty<string>();
}

public sealed class CSharpWorkflowCapabilitySafety
{
    public string VsixVerificationStatus { get; set; } = string.Empty;

    public string VersionSafetyStatus { get; set; } = string.Empty;

    public bool BridgeCapabilitiesVerified { get; set; }

    public bool AutomaticMutationRetryAllowed { get; set; }

    public string OperationStatusTool { get; set; } = string.Empty;

    public bool OperationStatusRequiresExplicitTarget { get; set; }

    public bool OperationStatusSupportsRecentListing { get; set; }

    public string[] Notes { get; set; } = Array.Empty<string>();
}
