using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class DiagnosticsNoisePolicyTests
{
    [Theory]
    [InlineData(@"D:\Repo\src\ACADPlugins\Legacy.cs")]
    [InlineData(@"D:\Repo\TZData_src\TimeZoneData.cs")]
    [InlineData(@"D:\Repo\obj\Debug\Generated.g.cs")]
    [InlineData(@"D:\Repo\src\Feature\Widget.Designer.cs")]
    public void ShouldFilterKnownNoisePath_WhenAutoAndUnscoped_ReturnsTrue(string filePath)
    {
        var request = new DiagnosticsRequest { NoiseProfile = CodeDiagnosticNoiseProfile.Auto };

        Assert.True(DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(request, filePath));
    }

    [Fact]
    public void ShouldFilterKnownNoisePath_WhenAutoAndFocused_ReturnsFalse()
    {
        var request = new DiagnosticsRequest
        {
            NoiseProfile = CodeDiagnosticNoiseProfile.Auto,
            ChangedFiles = new[] { @"src\ACADPlugins\Legacy.cs" },
        };

        Assert.False(DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(
            request,
            @"D:\Repo\src\ACADPlugins\Legacy.cs"));
    }

    [Fact]
    public void ShouldFilterKnownNoisePath_WhenFilterAndFocused_ReturnsTrue()
    {
        var request = new DiagnosticsRequest
        {
            NoiseProfile = CodeDiagnosticNoiseProfile.Filter,
            FilePath = @"D:\Repo\src\ACADPlugins\Legacy.cs",
        };

        Assert.True(DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(
            request,
            @"D:\Repo\src\ACADPlugins\Legacy.cs"));
    }

    [Fact]
    public void ShouldFilterKnownNoisePath_WhenOff_ReturnsFalse()
    {
        var request = new DiagnosticsRequest { NoiseProfile = CodeDiagnosticNoiseProfile.Off };

        Assert.False(DiagnosticsNoisePolicy.ShouldFilterKnownNoisePath(
            request,
            @"D:\Repo\TZData_src\TimeZoneData.cs"));
    }
}
