# Vicgital.Core.Caching

Provider-agnostic caching abstractions for Vicgital services, with in-memory and Redis
implementations that share a single contract. Write your caching code once against
`ICacheService`, and swap the backend per-environment (e.g. in-memory for local dev,
Redis for anything running more than one instance) without touching call sites.

## Packages

| Package | Description |
|---|---|
| [`Vicgital.Core.Caching`](src/Vicgital.Core.Caching) | Core abstractions: `ICacheService` and `CacheEntryOptions`. Every consumer depends on this. |
| [`Vicgital.Core.Caching.InMemory`](src/Vicgital.Core.Caching.InMemory) | In-process implementation backed by `Microsoft.Extensions.Caching.Memory`. |
| [`Vicgital.Core.Caching.Redis`](src/Vicgital.Core.Caching.Redis) | Distributed implementation backed by `StackExchange.Redis`. |

## Installation

Packages are published to GitHub Packages from [`.github/workflows/main.yml`](.github/workflows/main.yml).
Add the feed to your `NuGet.config`:

```xml
<packageSources>
  <add key="vicgital" value="https://nuget.pkg.github.com/vicgital/index.json" />
</packageSources>
<packageSourceCredentials>
  <vicgital>
    <add key="Username" value="%GH_PACKAGE_USERNAME%" />
    <add key="ClearTextPassword" value="%GH_PACKAGE_TOKEN%" />
  </vicgital>
</packageSourceCredentials>
```

Then reference whichever implementation(s) you need:

```bash
dotnet add package Vicgital.Core.Caching.InMemory
dotnet add package Vicgital.Core.Caching.Redis
```

## Usage

### The contract

```csharp
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
```

`CacheEntryOptions` exposes `AbsoluteExpirationRelativeToNow` and `SlidingExpiration`
(both optional; use `CacheEntryOptions.NoExpiration` for entries that should never expire).

Consumers should depend only on `Vicgital.Core.Caching` and take `ICacheService` via DI —
never reference `.InMemory` or `.Redis` directly from application code, so the backend
stays swappable.

### In-memory

```csharp
services.AddVicgitalInMemoryCaching();
```

Registers a singleton `ICacheService` backed by `IMemoryCache`. Scoped to a single
process — use this for local development or single-instance services.

### Redis

```csharp
services.AddVicgitalRedisCaching("localhost:6379", instanceName: "my-service:");
```

Or reuse a connection multiplexer you already manage (DI will not dispose it):

```csharp
services.AddVicgitalRedisCaching(existingConnectionMultiplexer, instanceName: "my-service:");
```

`instanceName` is prefixed onto every key, so multiple services can safely share one
Redis instance without key collisions.

### Reading and writing

```csharp
public sealed class ProductService(ICacheService cache)
{
    public Task<Product> GetProductAsync(int id, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"product:{id}",
            ct => productRepository.LoadAsync(id, ct),
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            ct);

    public void InvalidateProduct(int id) => cache.Remove($"product:{id}");

    public void InvalidateAllProducts() => cache.RemoveByPrefix("product:");
}
```

`GetOrCreateAsync` protects against cache-stampede (many callers recomputing the same
key at once) in both implementations, though the mechanism differs by design:

- **In-memory** uses a per-process semaphore keyed by cache key — sufficient since
  only one process ever holds this cache.
- **Redis** uses a distributed lock (`SET NX PX` + a Lua compare-and-delete release)
  so only one instance across your whole deployment recomputes the value; others wait
  briefly for the result rather than duplicating the work.

## Building

```bash
dotnet build Vicgital.Core.Caching.slnx
```
