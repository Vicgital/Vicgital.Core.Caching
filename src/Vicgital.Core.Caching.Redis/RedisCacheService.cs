using StackExchange.Redis;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Vicgital.Core.Caching.Abstractions;

namespace Vicgital.Core.Caching.Redis
{
    public sealed class RedisCacheService : ICacheService
    {
        private const string LockKeySuffix = ":__lock";
        private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);
        private static readonly string ReleaseLockScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;
        private readonly string _keyPrefix;

        public RedisCacheService(IConnectionMultiplexer connectionMultiplexer, RedisCacheOptions options)
        {
            ArgumentNullException.ThrowIfNull(connectionMultiplexer);
            ArgumentNullException.ThrowIfNull(options);

            _connectionMultiplexer = connectionMultiplexer;
            _database = connectionMultiplexer.GetDatabase();
            _keyPrefix = options.InstanceName ?? string.Empty;
        }

        public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var redisKey = BuildKeyString(key);
            var redisValue = _database.StringGet(redisKey);
            if (!redisValue.HasValue)
            {
                value = default;
                return false;
            }

            var entry = Deserialize<T>(redisValue);

            if (entry.SlidingExpiration is not null)
            {
                var ttl = ResolveTtl(entry.SlidingExpiration, entry.AbsoluteExpiration);
                if (ttl is not null)
                    _database.KeyExpire(redisKey, ttl);
            }

            value = entry.Value;
            return true;
        }

        public void Set<T>(string key, T value, CacheEntryOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var redisKey = BuildKeyString(key);
            var absoluteExpiration = options?.AbsoluteExpirationRelativeToNow is { } abs
                ? DateTimeOffset.UtcNow + abs
                : (DateTimeOffset?)null;

            var entry = new RedisCacheEntry<T>
            {
                Value = value,
                SlidingExpiration = options?.SlidingExpiration,
                AbsoluteExpiration = absoluteExpiration
            };

            var ttl = ResolveTtl(options?.SlidingExpiration, absoluteExpiration);
            _database.StringSet(redisKey, Serialize(entry), ttl, When.Always);
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

            var lockKey = BuildKeyString(key) + LockKeySuffix;
            var lockToken = Guid.NewGuid().ToString("N");
            var deadline = DateTime.UtcNow + LockWaitTimeout;

            while (true)
            {
                var acquired = await _database.StringSetAsync(lockKey, lockToken, LockExpiry, When.NotExists)
                    .ConfigureAwait(false);

                if (acquired)
                {
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
                        await _database.ScriptEvaluateAsync(ReleaseLockScript, [lockKey], [lockToken])
                            .ConfigureAwait(false);
                    }
                }

                if (TryGetValue<T>(key, out cached))
                    return cached;

                if (DateTime.UtcNow >= deadline)
                {
                    // Another instance is holding the lock longer than we're willing to wait; proceed
                    // without it rather than block indefinitely. Worst case is a duplicate factory call.
                    var value = await factory(cancellationToken).ConfigureAwait(false);
                    Set(key, value, options);
                    return value;
                }

                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Remove(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _database.KeyDelete(BuildKeyString(key));
        }

        public void RemoveByPrefix(string prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

            var pattern = (RedisValue)(BuildKeyString(prefix) + "*");

            foreach (var endpoint in _connectionMultiplexer.GetEndPoints())
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                if (server.IsReplica)
                    continue;

                foreach (var redisKey in server.Keys(_database.Database, pattern))
                    _database.KeyDelete(redisKey);
            }
        }

        private string BuildKeyString(string key) => string.IsNullOrEmpty(_keyPrefix) ? key : _keyPrefix + key;

        private static TimeSpan? ResolveTtl(TimeSpan? sliding, DateTimeOffset? absoluteExpiration)
        {
            var absoluteTtl = absoluteExpiration is { } abs ? abs - DateTimeOffset.UtcNow : (TimeSpan?)null;

            if (sliding is { } s && absoluteTtl is { } a)
                return s < a ? s : a;

            return sliding ?? absoluteTtl;
        }

        private static byte[] Serialize<T>(RedisCacheEntry<T> entry) =>
            JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions);

        private static RedisCacheEntry<T> Deserialize<T>(RedisValue redisValue)
        {
            var bytes = (byte[]?)redisValue
                ?? throw new InvalidOperationException("Cached value is empty.");

            return JsonSerializer.Deserialize<RedisCacheEntry<T>>(bytes, SerializerOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize cached value for type '{typeof(T)}'.");
        }
    }
}
