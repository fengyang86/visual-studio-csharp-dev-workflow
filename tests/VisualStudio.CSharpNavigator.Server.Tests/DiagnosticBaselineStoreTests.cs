using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class DiagnosticBaselineStoreTests
{
    [Fact]
    public void AddAndRead_PreservesScopeAndDiagnostics()
    {
        var store = new DiagnosticBaselineStore();
        var request = new DiagnosticsRequest
        {
            FilePath = @"D:\Repo\src\Feature\Widget.cs",
            NoisePathPatterns = new[] { "**/legacy/**" },
            MinimumSeverity = CodeDiagnosticSeverity.Warning,
        };
        var now = DateTimeOffset.UtcNow;
        var capture = store.Add(
            "snapshot:before",
            request,
            new[]
            {
                new CodeDiagnostic
                {
                    Id = "CS1001",
                    Message = "missing identifier",
                    ProjectName = "Feature",
                    Severity = CodeDiagnosticSeverity.Error,
                },
            },
            now);

        Assert.True(store.TryGet(capture.BaselineId, now.AddSeconds(1), out var entry, out var failure));
        Assert.Equal(string.Empty, failure);
        Assert.NotNull(entry);
        Assert.Equal("snapshot:before", entry!.WorkspaceVersion);
        Assert.Equal(capture.ScopeFingerprint, entry.ScopeFingerprint);
        Assert.Single(entry.Diagnostics);
    }

    [Fact]
    public void ExpiredBaseline_IsNotUsable()
    {
        var store = new DiagnosticBaselineStore();
        var capture = store.Add(
            "snapshot:before",
            new DiagnosticsRequest(),
            Array.Empty<CodeDiagnostic>(),
            DateTimeOffset.UtcNow.AddMinutes(-31));

        Assert.False(store.TryGet(capture.BaselineId, DateTimeOffset.UtcNow, out _, out var failure));
        Assert.Contains("NotFoundOrExpired", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeFingerprint_ExcludesTargetButIncludesNoiseConfiguration()
    {
        var first = new DiagnosticsRequest
        {
            Target = new VisualStudioBridgeTarget
            {
                InstanceId = "one",
                SolutionPath = @"D:\Repo\One.slnx",
            },
            NoisePathPatterns = new[] { "**/legacy/**" },
        };
        var sameScopeDifferentTarget = new DiagnosticsRequest
        {
            Target = new VisualStudioBridgeTarget
            {
                InstanceId = "two",
                SolutionPath = @"D:\Repo\Two.slnx",
            },
            NoisePathPatterns = new[] { "**/legacy/**" },
        };
        var differentScope = new DiagnosticsRequest
        {
            NoisePathPatterns = new[] { "**/generated/**" },
        };

        Assert.Equal(
            DiagnosticBaselineStore.CreateScopeFingerprint(first),
            DiagnosticBaselineStore.CreateScopeFingerprint(sameScopeDifferentTarget));
        Assert.NotEqual(
            DiagnosticBaselineStore.CreateScopeFingerprint(first),
            DiagnosticBaselineStore.CreateScopeFingerprint(differentScope));
    }
}
