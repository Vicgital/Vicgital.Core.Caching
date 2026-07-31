namespace Vicgital.Core.Caching.Redis
{
    public sealed class RedisCacheOptions
    {
        public required string Configuration { get; set; }

        public string? InstanceName { get; set; }
    }
}
