using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.Qdrant;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQdrantVectorStore(this IServiceCollection services)
    {
        services.AddHttpClient<IVectorStore, QdrantVectorStore>();
        return services;
    }
}
