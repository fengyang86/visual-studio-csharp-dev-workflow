using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class WorkspaceQueryResultTests
{
    [Theory]
    [InlineData("WorkspaceVersionChanged: the workspace changed during preview.", "WorkspaceVersionChanged")]
    [InlineData("MutationSessionNotFound: re-run preview_csharp_code_fix.", "MutationSessionNotFound")]
    [InlineData("SideEffectsNotAllowed: evaluation blocked.", "SideEffectsNotAllowed")]
    [InlineData("No colon in this diagnostic", null)]
    [InlineData("", null)]
    [InlineData("12345: purely numeric prefix", null)]
    [InlineData("Has Space: prefix with a space", null)]
    [InlineData("Has.Dot: prefix with a dot", null)]
    public void TryExtractErrorCode_FollowsCodeColonMessageConvention(string diagnostic, string? expected)
    {
        Assert.Equal(expected, WorkspaceQueryDiagnostics.TryExtractErrorCode(diagnostic));
    }

    [Fact]
    public void ErrorCode_IsNull_ForSucceededOrDiagnosticFreeResults()
    {
        var succeeded = new WorkspaceQueryResult<string>
        {
            Items = new[] { "value" },
            Diagnostics = new[] { "SomethingTruncated: kept the tail." },
            IsPartial = true,
        };
        Assert.True(succeeded.Succeeded);
        Assert.Null(succeeded.ErrorCode);

        var empty = new WorkspaceQueryResult<string>();
        Assert.True(empty.Succeeded);
        Assert.Null(empty.ErrorCode);
    }

    [Fact]
    public void ErrorCode_IsDerived_FromFirstDiagnosticOfFailedResult()
    {
        var failed = new WorkspaceQueryResult<string>
        {
            Diagnostics = new[] { "SymbolSearchNotFound: nothing matched 'Zzz'.", "Second diagnostic." },
            IsPartial = true,
            Succeeded = false,
        };

        Assert.False(failed.Succeeded);
        Assert.Equal("SymbolSearchNotFound", failed.ErrorCode);
    }

    [Fact]
    public void ErrorCode_IsNull_WhenFailureDiagnosticHasNoCodeShape()
    {
        var failed = new WorkspaceQueryResult<string>
        {
            Diagnostics = new[] { "Provide buildOutput, buildLogFilePath, or both." },
            IsPartial = true,
            Succeeded = false,
        };

        Assert.False(failed.Succeeded);
        Assert.Null(failed.ErrorCode);
    }
}
