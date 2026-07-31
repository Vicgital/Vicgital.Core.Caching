using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Vicgital.Core.Caching.Abstractions;

namespace Vicgital.Core.Caching.Redis.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVicgitalRedisCaching(
            this IServiceCollection services,
            string configuration,
            string? instanceName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

            return services.AddVicgitalRedisCaching(new RedisCacheOptions
            {
                Configuration = configuration,
                InstanceName = instanceName
            });
        }

        public static IServiceCollection AddVicgitalRedisCaching(
            this IServiceCollection services,
            RedisCacheOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Configuration);

            services.AddSingleton(options);
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.Configuration));
            services.AddSingleton<ICacheService, RedisCacheService>();

            return services;
        }

        public static IServiceCollection AddVicgitalRedisCaching(
            this IServiceCollection services,
            IConnectionMultiplexer connectionMultiplexer,
            string? instanceName = null)
        {
            ArgumentNullException.ThrowIfNull(connectionMultiplexer);

            services.AddSingleton(new RedisCacheOptions
            {
                Configuration = connectionMultiplexer.Configuration ?? string.Empty,
                InstanceName = instanceName
            });
            services.AddSingleton(connectionMultiplexer);
            services.AddSingleton<ICacheService, RedisCacheService>();

            return services;
        }
    }
}
