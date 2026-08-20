using System.Collections.Concurrent;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public sealed class ShortLivedQueryCache
{
    private const int DefaultMaxEntryCount = 256;

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<CacheEntry>>> _inFlight = new(StringComparer.Ordinal);
    private readonly int _maxEntryCount;
    private readonly TimeSpan _timeToLive;
    private long _hitCount;
    private long _missCount;
    private long _singleFlightJoinCount;

    public ShortLivedQueryCache()
        : this(TimeSpan.FromSeconds(5), DefaultMaxEntryCount)
    {
    }

    public ShortLivedQueryCache(TimeSpan timeToLive)
        : this(timeToLive, DefaultMaxEntryCount)
    {
    }

    public ShortLivedQueryCache(TimeSpan timeToLive, int maxEntryCount)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Cache TTL must be positive.");
        }

        if (maxEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntryCount), "Cache entry limit must be positive.");
        }

        _timeToLive = timeToLive;
        _maxEntryCount = maxEntryCount;
    }

    public async Task<CachedValue<T>> GetOrAddAsync<T>(
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(key, out var entry)
            && entry.ExpiresUtc > now
            && entry.Value is T cachedValue)
        {
            Interlocked.Increment(ref _hitCount);
            return new CachedValue<T>(cachedValue, true);
        }

        Interlocked.Increment(ref _missCount);

        var newFlight = new Lazy<Task<CacheEntry>>(
                async () =>
                {
                    var value = await factory().ConfigureAwait(false);
                    var created = DateTimeOffset.UtcNow;
                    var createdEntry = new CacheEntry(value, created.Add(_timeToLive));
                    _entries[key] = createdEntry;
                    Compact(created);
                    return createdEntry;
                },
                LazyThreadSafetyMode.ExecutionAndPublication);
        var lazyEntry = _inFlight.GetOrAdd(key, newFlight);
        var isFlightOwner = ReferenceEquals(lazyEntry, newFlight);
        if (!isFlightOwner)
        {
            Interlocked.Increment(ref _singleFlightJoinCount);
        }

        try
        {
            var entryFromFlight = await lazyEntry.Value.ConfigureAwait(false);
            return new CachedValue<T>((T)entryFromFlight.Value!, !isFlightOwner);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    public QueryCacheStatistics GetStatistics()
    {
        return new QueryCacheStatistics(
            Interlocked.Read(ref _hitCount),
            Interlocked.Read(ref _missCount),
            Interlocked.Read(ref _singleFlightJoinCount),
            _entries.Count,
            _inFlight.Count);
    }

    private void Compact(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresUtc <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        var overflowCount = _entries.Count - _maxEntryCount;
        if (overflowCount <= 0)
        {
            return;
        }

        foreach (var pair in _entries
            .OrderBy(pair => pair.Value.ExpiresUtc)
            .Take(overflowCount))
        {
            _entries.TryRemove(pair.Key, out _);
        }
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset ExpiresUtc);
}

public readonly record struct CachedValue<T>(T Value, bool IsCacheHit);

public readonly record struct QueryCacheStatistics(
    long HitCount,
    long MissCount,
    long SingleFlightJoinCount,
    int EntryCount,
    int InFlightCount);
