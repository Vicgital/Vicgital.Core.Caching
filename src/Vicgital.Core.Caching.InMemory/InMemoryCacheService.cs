using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Vicgital.Core.Caching.Abstractions;

namespace Vicgital.Core.Caching.InMemory
{
    public sealed class InMemoryCacheService(IMemoryCache cache) : ICacheService
    {
        private readonly IMemoryCache _cache = cache;
        private readonly ConcurrentDictionary<string, byte> _keys = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

        public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return _cache.TryGetValue(key, out value);
        }

        public void Set<T>(string key, T value, CacheEntryOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            _cache.Set(key, value, ToMemoryCacheEntryOptions(options));
            _keys.TryAdd(key, 0);
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            CacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(factory);

            if (TryGetValue<T>(key, out var cached))
                return cached;

            var keyLock = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (TryGetValue<T>(key, out cached))
                    return cached;

                var value = await factory(cancellationToken).ConfigureAwait(false);
                Set(key, value, options);
                return value;
            }
            finally
            {
                keyLock.Release();
            }
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }

        public void RemoveByPrefix(string prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

            foreach (var key in _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                Remove(key);
        }

        private MemoryCacheEntryOptions ToMemoryCacheEntryOptions(CacheEntryOptions? options)
        {
            var memoryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options?.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = options?.SlidingExpiration
            };

            memoryOptions.RegisterPostEvictionCallback(OnEvicted);

            return memoryOptions;
        }

        private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
        {
            var evictedKey = (string)key;
            _keys.TryRemove(evictedKey, out _);
            _keyLocks.TryRemove(evictedKey, out _);
        }
    }
}
