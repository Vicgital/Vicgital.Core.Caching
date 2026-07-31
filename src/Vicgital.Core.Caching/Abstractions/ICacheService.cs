using System.Diagnostics.CodeAnalysis;

namespace Vicgital.Core.Caching.Abstractions
{
    public interface ICacheService
    {
        bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value);

        void Set<T>(string key, T value, CacheEntryOptions? options = null);

        Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            CacheEntryOptions? options = null,
            CancellationToken cancellationToken = default);

        void Remove(string key);

        void RemoveByPrefix(string prefix);
    }
}
