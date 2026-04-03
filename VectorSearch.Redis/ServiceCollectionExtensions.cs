using Microsoft.Extensions.DependencyInjection;
using VectorSearch.Core;

namespace VectorSearch.Redis;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedisVectorStore(this IServiceCollection services)
    {
        services.AddScoped<IVectorStore, RedisVectorStore>();
        return services;
    }
}
