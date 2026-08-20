namespace VisualStudio.CSharpNavigator.Server.Bridge;

public sealed class NamedPipeBridgeOptions
{
    public const string ConfigurationSectionName = "VisualStudioBridge";

    public string PipeName { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public string SolutionPath { get; set; } = string.Empty;

    public string DiscoveryDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualStudio.CSharpNavigatorMcp",
        "instances");

    public int DiscoveryStaleAfterSeconds { get; set; } = 30;

    public int ConnectTimeoutMilliseconds { get; set; } = 2000;
}
