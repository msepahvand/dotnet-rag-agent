using Amazon.BedrockRuntime;
using Amazon.S3Vectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using VectorSearch.Core;

namespace VectorSearch.S3;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectorSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = VectorSearchOptionsValidator.Parse(configuration);

        // Required by Bedrock chat/embedding connectors regardless of vector store provider.
        services.AddAWSService<IAmazonBedrockRuntime>();

        services.AddHttpClient<IPostService, HackerNewsService>(client =>
        {
            client.BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/");
        });

        // Configure Semantic Kernel + embedding pipeline once for all providers.
        services.AddBedrockEmbeddingGenerator(options.EmbeddingModelId);
        services.AddBedrockChatCompletionService(options.ChatModelId);
        services.AddScoped<IFunctionInvocationFilter, ToolInvocationFilter>();
        services.AddTransient(sp =>
        {
            var kernel = new Kernel(sp);

            foreach (var filter in sp.GetServices<IFunctionInvocationFilter>())
            {
                kernel.FunctionInvocationFilters.Add(filter);
            }

            return kernel;
        });
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<SemanticSearchPlugin>();

        RegisterVectorStore(services, options.VectorStoreProvider);

        // Register the main vector service
        services.AddScoped<IVectorService, VectorService>();
        services.AddScoped<IAgentAnswerService, GroundedAgentAnswerService>();

        return services;
    }

    private static void RegisterVectorStore(
        IServiceCollection services,
        VectorStoreProvider vectorProvider)
    {
        switch (vectorProvider)
        {
            case VectorStoreProvider.Qdrant:
                services.AddHttpClient<IVectorStore, QdrantVectorStore>();
                break;

            case VectorStoreProvider.Redis:
                // Late-bind Redis implementation so this project can compile/run without a hard project reference.
                services.AddScoped<IVectorStore>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var redisStoreType = Type.GetType("VectorSearch.Redis.RedisVectorStore, VectorSearch.Redis");
                    if (redisStoreType == null)
                    {
                        throw new InvalidOperationException("Redis provider requires VectorSearch.Redis project reference");
                    }

                    return (IVectorStore)Activator.CreateInstance(redisStoreType, config)!;
                });
                break;

            case VectorStoreProvider.S3Vectors:
            default:
                services.AddAWSService<IAmazonS3Vectors>();
                services.AddScoped<IVectorStore, S3VectorStore>();
                break;
        }
    }
}
