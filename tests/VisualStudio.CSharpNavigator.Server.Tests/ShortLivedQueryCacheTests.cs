using VisualStudio.CSharpNavigator.Server.Tools;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class ShortLivedQueryCacheTests
{
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
}
