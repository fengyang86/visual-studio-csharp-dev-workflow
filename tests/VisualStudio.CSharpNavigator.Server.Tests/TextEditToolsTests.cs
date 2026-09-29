using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class TextEditToolsTests
{
    [Fact]
    public async Task PreviewCSharpTextEdits_ForwardsRequestToBridge()
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new TextEditTools(bridge);
        var edits = new[]
        {
            new CSharpTextEdit { StartLine = 3, StartColumn = 5, EndLine = 3, EndColumn = 9, NewText = "renamed" },
        };

        var result = await tools.PreviewCSharpTextEdits(
            @"D:\Repo\src\A.cs",
            edits,
            maxTextChanges: 120,
            maxSnippetLength: 250,
            includeGeneratedCode: true,
            targetInstanceId: "instance-1");

        Assert.True(result.Succeeded);
        Assert.NotNull(bridge.LastTextEditPreviewRequest);
        Assert.Equal(@"D:\Repo\src\A.cs", bridge.LastTextEditPreviewRequest.FilePath);
        Assert.Equal("instance-1", bridge.LastTextEditPreviewRequest.Target?.InstanceId);
        Assert.Equal(120, bridge.LastTextEditPreviewRequest.MaxTextChanges);
        Assert.Equal(250, bridge.LastTextEditPreviewRequest.MaxSnippetLength);
        Assert.True(bridge.LastTextEditPreviewRequest.IncludeGeneratedCode);
        var edit = Assert.Single(bridge.LastTextEditPreviewRequest.Edits);
        Assert.Equal("renamed", edit.NewText);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(5, 1, 2, 1)]
    [InlineData(2, 9, 2, 4)]
    public async Task PreviewCSharpTextEdits_RejectsInvalidRanges(int startLine, int startColumn, int endLine, int endColumn)
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new TextEditTools(bridge);
        var edits = new[]
        {
            new CSharpTextEdit { StartLine = startLine, StartColumn = startColumn, EndLine = endLine, EndColumn = endColumn, NewText = "x" },
        };

        var result = await tools.PreviewCSharpTextEdits(@"D:\Repo\src\A.cs", edits);

        Assert.False(result.Succeeded);
        Assert.Equal("TextEditInvalidRange", result.ErrorCode);
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task PreviewCSharpTextEdits_RejectsEmptyEdits()
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new TextEditTools(bridge);

        var result = await tools.PreviewCSharpTextEdits(@"D:\Repo\src\A.cs", Array.Empty<CSharpTextEdit>());

        Assert.False(result.Succeeded);
        Assert.Equal("EditsRequired", result.ErrorCode);
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ApplyCSharpTextEdits_RequiresExplicitTarget()
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new TextEditTools(bridge);
        var edits = new[] { new CSharpTextEdit { StartLine = 1, StartColumn = 1, EndLine = 1, EndColumn = 2, NewText = "x" } };

        var result = await tools.ApplyCSharpTextEdits(@"D:\Repo\src\A.cs", edits);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("requires an explicit target"));
        Assert.Equal(0, bridge.CallCount);
    }

    [Fact]
    public async Task ApplyCSharpTextEdits_ForwardsAllowFlagsAndTarget()
    {
        var bridge = new CodeNavigationToolsTests.RecordingBridge();
        var tools = new TextEditTools(bridge);
        var edits = new[] { new CSharpTextEdit { StartLine = 1, StartColumn = 1, EndLine = 1, EndColumn = 2, NewText = "x" } };

        var result = await tools.ApplyCSharpTextEdits(
            @"D:\Repo\src\A.cs",
            edits,
            allowGeneratedDocumentChanges: true,
            allowUnsupportedDocumentChanges: true,
            allowTruncatedPreview: true,
            targetSolutionPath: @"D:\Repo\Repo.sln");

        Assert.True(result.Succeeded);
        Assert.NotNull(bridge.LastTextEditApplyRequest);
        Assert.Equal(@"D:\Repo\Repo.sln", bridge.LastTextEditApplyRequest.Target?.SolutionPath);
        Assert.True(bridge.LastTextEditApplyRequest.AllowGeneratedDocumentChanges);
        Assert.True(bridge.LastTextEditApplyRequest.AllowUnsupportedDocumentChanges);
        Assert.True(bridge.LastTextEditApplyRequest.AllowTruncatedPreview);
        var edit = Assert.Single(bridge.LastTextEditApplyRequest.Edits);
        Assert.Equal("x", edit.NewText);
    }
}
