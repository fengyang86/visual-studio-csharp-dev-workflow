using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class ShortLivedQueryCacheTests
{
    [Fact]
    public async Task LastWaiterCancellationStopsSharedWorkAndNewCallerDoesNotJoinAbandonedWork()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        using var ownerCancellation = new CancellationTokenSource();
        var abandonedGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandoned = cache.GetOrAddAsync("key", async token =>
        {
            using var registration = token.Register(() => observedCancellation.TrySetResult());
            return await abandonedGate.Task;
        }, ownerCancellation.Token);
        var sharedWork = ReadSharedWork(cache, "key");
        ownerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, (await cache.GetOrAddAsync("key", _ => Task.FromResult(42), default)).Value);
        abandonedGate.SetResult(99);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sharedWork);
        Assert.Equal(42, (await cache.GetOrAddAsync("key", _ => Task.FromResult(-1), default)).Value);
    }

    [Fact]
    public async Task AbandonedWorkPausedBeforeCommitCannotOverwriteReplacementCache()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        using var resumeCommit = new ManualResetEventSlim();
        var factoryResult = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = cache.GetOrAddAsync("key", _ => factoryResult.Task, cancellation.Token, shouldCache: _ =>
        {
            reachedCommit.TrySetResult();
            Assert.True(resumeCommit.Wait(TimeSpan.FromSeconds(5)));
            return true;
        });
        var sharedWork = ReadSharedWork(cache, "key");
        factoryResult.SetResult(99);
        try
        {
            await reachedCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(42, (await cache.GetOrAddAsync("key", _ => Task.FromResult(42), default)).Value);
        }
        finally
        {
            resumeCommit.Set();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sharedWork);
        Assert.Equal(42, (await cache.GetOrAddAsync("key", _ => Task.FromResult(-1), default)).Value);
    }

    [Fact]
    public async Task CompletionRacingWithLastWaiterCancellationDoesNotAccessDisposedTokenSource()
    {
        for (var i = 0; i < 100; i++)
        {
            var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
            using var cancellation = new CancellationTokenSource();
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = cache.GetOrAddAsync("key", token => gate.Task.WaitAsync(token), cancellation.Token);
            var cancel = Task.Run(() => cancellation.Cancel());
            gate.TrySetResult(42);
            try
            {
                Assert.Equal(42, (await pending).Value);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }

            await cancel;
            Assert.Equal(0, cache.GetStatistics().InFlightCount);
        }
    }

    [Fact]
    public async Task CancelingOneWaiterDoesNotCancelTheSharedComputation()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        using var ownerCancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var owner = cache.GetOrAddAsync("key", async token =>
        {
            Interlocked.Increment(ref calls);
            return await gate.Task.WaitAsync(token);
        }, ownerCancellation.Token);
        var follower = cache.GetOrAddAsync("key", _ => Task.FromResult(-1), CancellationToken.None);
        ownerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
        Assert.False(follower.IsCompleted);
        gate.SetResult(42);
        Assert.Equal(42, (await follower.WaitAsync(TimeSpan.FromSeconds(5))).Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CanceledFollowerStopsWaitingWithoutCancelingOwner()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        using var followerCancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = cache.GetOrAddAsync("key", token => gate.Task.WaitAsync(token), CancellationToken.None);
        var follower = cache.GetOrAddAsync("key", _ => Task.FromResult(-1), followerCancellation.Token);
        followerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower);
        gate.SetResult(42);
        Assert.Equal(42, (await owner.WaitAsync(TimeSpan.FromSeconds(5))).Value);
    }

    [Fact]
    public async Task FailedRequestCanBeRetriedWithoutReusingItsFlight()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync<int>(
            "key", _ => Task.FromException<int>(new InvalidOperationException("失败样本")), CancellationToken.None));
        Assert.Equal(42, (await cache.GetOrAddAsync("key", _ => Task.FromResult(42), CancellationToken.None)).Value);
        Assert.Equal(0, cache.GetStatistics().InFlightCount);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenEntryExpires_RecomputesValue()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMilliseconds(20));
        var factoryCalls = 0;

        var first = await cache.GetOrAddAsync(
            "search",
            () => Task.FromResult(++factoryCalls),
            CancellationToken.None);
        await Task.Delay(80);
        var second = await cache.GetOrAddAsync(
            "search",
            () => Task.FromResult(++factoryCalls),
            CancellationToken.None);

        Assert.False(first.IsCacheHit);
        Assert.False(second.IsCacheHit);
        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenEntryLimitIsExceeded_EvictsOlderEntry()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1), maxEntryCount: 1);
        var firstKeyCalls = 0;
        var secondKeyCalls = 0;

        var first = await cache.GetOrAddAsync(
            "first",
            () => Task.FromResult(++firstKeyCalls),
            CancellationToken.None);
        var second = await cache.GetOrAddAsync(
            "second",
            () => Task.FromResult(++secondKeyCalls),
            CancellationToken.None);
        var firstAgain = await cache.GetOrAddAsync(
            "first",
            () => Task.FromResult(++firstKeyCalls),
            CancellationToken.None);

        Assert.False(first.IsCacheHit);
        Assert.False(second.IsCacheHit);
        Assert.False(firstAgain.IsCacheHit);
        Assert.Equal(2, firstKeyCalls);
        Assert.Equal(1, secondKeyCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_WhenConcurrentRequestsUseTheSameKey_ExecutesFactoryOnce()
    {
        var cache = new ShortLivedQueryCache(TimeSpan.FromMinutes(1));
        var factoryCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<CachedValue<int>> LoadAsync() => cache.GetOrAddAsync(
            "same-key",
            async () =>
            {
                Interlocked.Increment(ref factoryCalls);
                await gate.Task;
                return 42;
            },
            CancellationToken.None);

        var first = LoadAsync();
        var second = LoadAsync();
        gate.SetResult();
        var values = await Task.WhenAll(first, second);

        Assert.Equal(1, factoryCalls);
        Assert.All(values, value => Assert.Equal(42, value.Value));
        Assert.Contains(values, value => !value.IsCacheHit);
        Assert.Contains(values, value => value.IsCacheHit);
    }

    private static Task ReadSharedWork(ShortLivedQueryCache cache, string key)
    {
        // 等待真实后台任务结束后再断言，避免只等待已经取消的调用方而漏掉迟到写入。
        var field = typeof(ShortLivedQueryCache).GetField("_inFlight",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var flights = (System.Collections.IDictionary)field.GetValue(cache)!;
        var flight = flights[key]!;
        var lazy = flight.GetType().GetProperty("Work")!.GetValue(flight)!;
        return (Task)lazy.GetType().GetProperty("Value")!.GetValue(lazy)!;
    }
}
