using System.Collections.Concurrent;

namespace VisualStudio.CSharpNavigator.Server.Tools;

public sealed class ShortLivedQueryCache
{
    private const int DefaultMaxEntryCount = 256;

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FlightState> _inFlight = new(StringComparer.Ordinal);
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
        return await GetOrAddAsync(key, _ => factory(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<CachedValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        Func<T, bool>? shouldCache = null)
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

        FlightState lazyEntry;
        bool isFlightOwner;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newFlight = new FlightState(
                async (flight, lifetimeToken) =>
                {
                    var value = await factory(lifetimeToken).ConfigureAwait(false);
                    lifetimeToken.ThrowIfCancellationRequested();
                    var created = DateTimeOffset.UtcNow;
                    var createdEntry = new CacheEntry(value, created.Add(_timeToLive));
                    if (shouldCache?.Invoke(value) != false)
                    {
                        flight.TryCommit(() =>
                        {
                            if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, flight))
                            {
                                _entries[key] = createdEntry;
                                Compact(created);
                            }
                        });
                    }

                    lifetimeToken.ThrowIfCancellationRequested();
                    return createdEntry;
                });
            lazyEntry = _inFlight.GetOrAdd(key, newFlight);
            isFlightOwner = ReferenceEquals(lazyEntry, newFlight);
            if (!isFlightOwner)
            {
                newFlight.Complete();
            }

            if (lazyEntry.TryJoin())
            {
                break;
            }

            ((ICollection<KeyValuePair<string, FlightState>>)_inFlight)
                .Remove(new KeyValuePair<string, FlightState>(key, lazyEntry));
        }

        if (!isFlightOwner)
        {
            Interlocked.Increment(ref _singleFlightJoinCount);
        }

        var work = lazyEntry.Work.Value;
        if (isFlightOwner)
        {
            _ = work.ContinueWith(completed =>
            {
                _ = completed.Exception;
                ((ICollection<KeyValuePair<string, FlightState>>)_inFlight)
                    .Remove(new KeyValuePair<string, FlightState>(key, lazyEntry));
                lazyEntry.Complete();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        try
        {
            var entryFromFlight = await work.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new CachedValue<T>((T)entryFromFlight.Value!, !isFlightOwner);
        }
        finally
        {
            if (lazyEntry.Leave(work.IsCompleted))
            {
                // 没有等待方时停止共享计算，并把取消传递给命名管道桥接。
                ((ICollection<KeyValuePair<string, FlightState>>)_inFlight)
                    .Remove(new KeyValuePair<string, FlightState>(key, lazyEntry));
                lazyEntry.Cancel();
            }
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

    private sealed class FlightState
    {
        private readonly object _gate = new();
        private bool _accepting = true;
        private int _waiterCount;

        public FlightState(Func<FlightState, CancellationToken, Task<CacheEntry>> factory)
        {
            // The flight lifetime must accommodate the longest bridge call: a full
            // diagnostics scan can legally spend up to 55s (VSIX budget) plus the
            // status round trip and pipe connect, so the old 60s hard deadline
            // discarded fully computed analyzer runs at the margin.
            Lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            Work = new Lazy<Task<CacheEntry>>(
                () => factory(this, Lifetime.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public CancellationTokenSource Lifetime { get; }

        public Lazy<Task<CacheEntry>> Work { get; }

        public bool TryJoin()
        {
            lock (_gate)
            {
                if (!_accepting)
                {
                    return false;
                }

                _waiterCount++;
                return true;
            }
        }

        public bool Leave(bool completed)
        {
            lock (_gate)
            {
                if (--_waiterCount != 0 || completed || !_accepting)
                {
                    return false;
                }

                _accepting = false;
                return true;
            }
        }

        public void TryCommit(Action commit)
        {
            lock (_gate)
            {
                if (_accepting && !Lifetime.IsCancellationRequested)
                {
                    commit();
                }
            }
        }

        public void Cancel()
        {
            try
            {
                Lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 计算完成后释放与最后等待方取消可以并发，完成状态已经满足清理目标。
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                _accepting = false;
                Lifetime.Dispose();
            }
        }
    }
}

public readonly record struct CachedValue<T>(T Value, bool IsCacheHit);

public readonly record struct QueryCacheStatistics(
    long HitCount,
    long MissCount,
    long SingleFlightJoinCount,
    int EntryCount,
    int InFlightCount);
