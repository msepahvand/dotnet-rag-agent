using RagAgent.Redis;
using RagAgent.Agents;

namespace RagAgent.Api.Extensions;

public static class VectorStoreRegistrationExtensions
{
    public static IServiceCollection AddVectorStoreProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = VectorSearchOptionsValidator.ParseProvider(configuration);

        return provider switch
        {
            VectorStoreProvider.Qdrant => services.AddQdrantVectorStore(),
            VectorStoreProvider.Redis => services.AddRedisVectorStore(),
            _ => services.AddS3VectorsVectorStore()
        };
    }
}
