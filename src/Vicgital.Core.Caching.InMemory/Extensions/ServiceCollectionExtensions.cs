using Microsoft.Extensions.DependencyInjection;
using Vicgital.Core.Caching.Abstractions;

namespace Vicgital.Core.Caching.InMemory.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddVicgitalInMemoryCaching(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, InMemoryCacheService>();

            return services;
        }
    }

}
