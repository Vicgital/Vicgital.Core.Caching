namespace Vicgital.Core.Caching.Redis
{
    internal sealed class RedisCacheEntry<T>
    {
        public required T Value { get; init; }

        public TimeSpan? SlidingExpiration { get; init; }

        public DateTimeOffset? AbsoluteExpiration { get; init; }
    }
}
