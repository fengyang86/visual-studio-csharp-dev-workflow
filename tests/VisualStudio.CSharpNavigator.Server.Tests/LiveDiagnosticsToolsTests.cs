using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class LiveDiagnosticsToolsTests
{
    [Fact]
    public async Task GetCSharpLiveDiagnostics_ForwardsScopeAndTarget()
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new BuildDiagnosticsTools(new CodeNavigationTools(bridge), bridge);

        var result = await tools.GetCSharpLiveDiagnostics(
            filePath: @"D:\Repo\src\A.cs",
            projectName: "Repo.Core",
            includePathPatterns: new[] { @"D:\Repo\src\**" },
            excludePathPatterns: new[] { @"D:\Repo\src\Generated\**" },
            changedFiles: new[] { @"D:\Repo\src\A.cs" },
            minimumSeverity: CodeDiagnosticSeverity.Warning,
            maxResults: 120,
            includeGeneratedCode: true,
            targetInstanceId: "instance-1");

        Assert.True(result.Succeeded);
        Assert.NotNull(bridge.LastLiveDiagnosticsRequest);
        var forwarded = bridge.LastLiveDiagnosticsRequest;
        Assert.Equal(@"D:\Repo\src\A.cs", forwarded.FilePath);
        Assert.Equal("Repo.Core", forwarded.ProjectName);
        Assert.Equal(new[] { @"D:\Repo\src\**" }, forwarded.IncludePathPatterns);
        Assert.Equal(new[] { @"D:\Repo\src\Generated\**" }, forwarded.ExcludePathPatterns);
        Assert.Equal(new[] { @"D:\Repo\src\A.cs" }, forwarded.ChangedFiles);
        Assert.Equal(CodeDiagnosticSeverity.Warning, forwarded.MinimumSeverity);
        Assert.Equal(120, forwarded.MaxResults);
        Assert.True(forwarded.IncludeGeneratedCode);
        Assert.Equal("instance-1", forwarded.Target?.InstanceId);
    }
}
