namespace Vicgital.Core.Caching
{
    public sealed class CacheEntryOptions
    {
        public static readonly CacheEntryOptions NoExpiration = new();

        public TimeSpan? AbsoluteExpirationRelativeToNow { get; init; }
        public TimeSpan? SlidingExpiration { get; init; }
    }
}
