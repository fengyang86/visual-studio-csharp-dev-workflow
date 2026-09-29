using System.Text.Json;
using VisualStudio.CSharpNavigator.Vsix.Bridge;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class PipeBridgeRequestDispatcherTests
{
    private const string Success = """{"protocolVersion":"1","result":{"items":[],"isPartial":false,"diagnostics":[]}}""";

    [Fact]
    public async Task CancellationBeforeRegistrationPreventsExecution()
    {
        var calls = 0;
        var dispatcher = Create((_, _) => { calls++; return Task.FromResult(Success); });
        await dispatcher.ExecuteAsync(Request("", "CancelRequest", new { requestId = "first" }), default);
        var response = await dispatcher.ExecuteAsync(Request("first", "ApplyRename"), default);
        Assert.Contains("RequestCanceledBeforeStart", response);
        Assert.Equal(0, calls);
        Assert.Equal("CanceledBeforeStart", (await Status(dispatcher, "first")).GetProperty("state").GetString());
    }

    [Fact]
    public async Task DuplicateRunningAndCompletedRequestsNeverExecuteTwice()
    {
        var calls = 0;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Create((_, _) => { calls++; return release.Task; });
        var request = Request("first", "ApplyRename");
        var first = dispatcher.ExecuteAsync(request, default);
        Assert.Contains("BridgeRequestAlreadyRunning", await dispatcher.ExecuteAsync(request, default));
        Assert.Contains("BridgeRequestIdConflict", await dispatcher.ExecuteAsync(Request("first", "ApplyCleanup"), default));
        release.SetResult(Success);
        var response = await first;
        Assert.Equal(response, await dispatcher.ExecuteAsync(request, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationAfterDispatchIsUnknownNotProofOfRollback()
    {
        var dispatcher = Create(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Success;
        });
        var pending = dispatcher.ExecuteAsync(Request("first", "StepOver"), default);
        await dispatcher.ExecuteAsync(Request("", "CancelRequest", new { requestId = "first" }), default);
        Assert.Contains("OperationOutcomeUnknown", await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        var status = await Status(dispatcher, "first");
        Assert.Equal("OutcomeUnknown", status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("cancellationRequested").GetBoolean());
        Assert.True(status.GetProperty("isTerminal").GetBoolean());
    }

    [Fact]
    public async Task CancellationAfterCompletionDoesNotReplaceSuccessfulResponse()
    {
        var dispatcher = Create((_, _) => Task.FromResult(Success));
        await dispatcher.ExecuteAsync(Request("first", "StepOver"), default);
        for (var i = 0; i < 20; i++)
        {
            Assert.Contains("BridgeRequestAlreadyTerminal",
                await dispatcher.ExecuteAsync(Request("", "CancelRequest", new { requestId = "first" }), default));
        }

        var status = await Status(dispatcher, "first");
        Assert.Equal("Completed", status.GetProperty("state").GetString());
        Assert.Equal(Success, status.GetProperty("responseJson").GetString());
    }

    [Fact]
    public async Task ConcurrentCancellationAndCompletionKeepAQueryableTerminalResult()
    {
        for (var i = 0; i < 100; i++)
        {
            var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = Create((_, _) => release.Task);
            var pending = dispatcher.ExecuteAsync(Request("first", "StepOver"), default);
            var cancel = Task.Run(() => dispatcher.ExecuteAsync(Request("", "CancelRequest", new { requestId = "first" }), default));
            release.SetResult(Success);
            await Task.WhenAll(pending, cancel).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Completed", (await Status(dispatcher, "first")).GetProperty("state").GetString());
        }
    }

    [Fact]
    public async Task CapacityDoesNotEvictLiveOperationsAndExpiredIsExplicit()
    {
        var now = DateTimeOffset.UtcNow;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new PipeBridgeRequestDispatcher("test", (_, _) => release.Task,
            (_, _) => Task.FromResult<string?>(null), capacity: 1, retention: TimeSpan.FromSeconds(1), utcNow: () => now);
        var running = dispatcher.ExecuteAsync(Request("first", "StepOver"), default);
        Assert.Contains("BridgeRequestCapacityExceeded", await dispatcher.ExecuteAsync(Request("second", "StepOver"), default));
        Assert.Equal("Running", (await Status(dispatcher, "first")).GetProperty("state").GetString());
        release.SetResult(Success);
        await running;
        now = now.AddSeconds(2);
        var query = await dispatcher.ExecuteAsync(Request("", "GetOperationStatus", new { requestId = "first" }), default);
        Assert.Contains("OperationNotFoundOrExpired", query);
        using var document = JsonDocument.Parse(query);
        Assert.True(document.RootElement.GetProperty("result").GetProperty("isPartial").GetBoolean());
    }

    [Fact]
    public async Task LargeResponsesAreOmittedButCannotTriggerReplay()
    {
        var calls = 0;
        var dispatcher = new PipeBridgeRequestDispatcher("test", (_, _) => { calls++; return Task.FromResult(Success); },
            (_, _) => Task.FromResult<string?>(null), maxResponseCharacters: 10);
        await dispatcher.ExecuteAsync(Request("first", "StepOver"), default);
        Assert.True((await Status(dispatcher, "first")).GetProperty("responseOmitted").GetBoolean());
        Assert.Contains("BridgeOperationResponseOmitted", await dispatcher.ExecuteAsync(Request("first", "StepOver"), default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CapacityEvictsCompletedRecordsButPreservesRunningOnes()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new PipeBridgeRequestDispatcher("test",
            (request, _) => request.RequestId == "running" ? release.Task : Task.FromResult(Success),
            (_, _) => Task.FromResult<string?>(null), capacity: 2);
        var running = dispatcher.ExecuteAsync(Request("running", "StepOver"), default);
        await dispatcher.ExecuteAsync(Request("completed", "StepOver"), default);
        await dispatcher.ExecuteAsync(Request("replacement", "StepOver"), default);
        Assert.Equal("Running", (await Status(dispatcher, "running")).GetProperty("state").GetString());
        Assert.Equal("Completed", (await Status(dispatcher, "replacement")).GetProperty("state").GetString());
        Assert.Contains("OperationNotFoundOrExpired",
            await dispatcher.ExecuteAsync(Request("", "GetOperationStatus", new { requestId = "completed" }), default));
        release.SetResult(Success);
        await running;
    }

    [Fact]
    public async Task TargetValidationAppliesToRecoveryQueries()
    {
        var calls = 0;
        var dispatcher = new PipeBridgeRequestDispatcher("test", (_, _) => { calls++; return Task.FromResult(Success); },
            (_, _) => Task.FromResult<string?>("""{"error":{"code":"WorkspaceTargetMismatch"}}"""));
        Assert.Contains("WorkspaceTargetMismatch", await dispatcher.ExecuteAsync(Request("", "GetOperationStatus"), default));
        Assert.Contains("WorkspaceTargetMismatch", await dispatcher.ExecuteAsync(Request("", "CancelRequest"), default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ReadQueriesAreNotRetainedAndMissingIdsRemainCompatible()
    {
        var dispatcher = Create((_, _) => Task.FromResult(Success));
        Assert.Equal(Success, await dispatcher.ExecuteAsync(Request("read", "SearchSymbols"), default));
        Assert.Contains("OperationNotFoundOrExpired",
            await dispatcher.ExecuteAsync(Request("", "GetOperationStatus", new { requestId = "read" }), default));
        using var result = JsonDocument.Parse(await dispatcher.ExecuteAsync(Request("", "ApplyRename"), default));
        Assert.False(string.IsNullOrWhiteSpace(result.RootElement.GetProperty("operationId").GetString()));
    }

    [Fact]
    public async Task WorkspaceMutationsSerializeWithEachOther()
    {
        var firstDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new PipeBridgeRequestDispatcher("test",
            (request, _) =>
            {
                if (request.RequestId == "first")
                {
                    firstDispatched.TrySetResult();
                    return releaseFirst.Task;
                }

                secondDispatched.TrySetResult();
                return Task.FromResult(Success);
            },
            (_, _) => Task.FromResult<string?>(null), capacity: 8);

        var first = dispatcher.ExecuteAsync(Request("first", "ApplyRename"), default);
        await firstDispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = dispatcher.ExecuteAsync(Request("second", "ApplyCleanup"), default);
        await Task.Delay(200);
        Assert.False(secondDispatched.Task.IsCompleted);

        releaseFirst.SetResult(Success);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondDispatched.Task.IsCompleted);
    }

    private static PipeBridgeRequestDispatcher Create(Func<PipeBridgeRequestEnvelope, CancellationToken, Task<string>> dispatch)
        => new("test", dispatch, (_, _) => Task.FromResult<string?>(null));

    internal static string Request(string requestId, string method, object? payload = null)
        => JsonSerializer.Serialize(new { protocolVersion = "1", requestId, method, payload = payload ?? new { } });

    private static async Task<JsonElement> Status(PipeBridgeRequestDispatcher dispatcher, string requestId)
    {
        using var result = JsonDocument.Parse(await dispatcher.ExecuteAsync(
            Request("", "GetOperationStatus", new { requestId, includeResponse = true }), default));
        return Assert.Single(result.RootElement.GetProperty("result").GetProperty("items").EnumerateArray()).Clone();
    }
}
