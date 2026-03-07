using System.Collections.Concurrent;

using Microsoft.Extensions.Caching.Hybrid;

namespace NpmCdn.PerformanceTests;

public class FakeHybridCache : HybridCache
{
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            return new ValueTask<T>((T)cachedValue);
        }

        var tcs = new TaskCompletionSource<T>();

        var factoryTask = factory(state, cancellationToken).AsTask();
        return new ValueTask<T>(factoryTask.ContinueWith(t =>
        {
            var value = t.Result;
            _cache[key] = value!;
            return value;
        }));
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Tags not supported in FakeHybridCache.");
    }

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (value is not null)
        {
            _cache[key] = value;
        }
        return ValueTask.CompletedTask;
    }
}
