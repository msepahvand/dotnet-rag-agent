using Amazon.BedrockRuntime;
using Amazon.S3Vectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using VectorSearch.Core;
using VectorSearch.S3.Agents;

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

        // Register the main vector service
        services.AddScoped<IVectorService, VectorService>();

        // Multi-agent: researcher retrieves, writer synthesises
        services.AddScoped<ResearcherAgent>();
        services.AddScoped<WriterAgent>();
        services.AddScoped<IAgentAnswerService, MultiAgentAnswerService>();

        return services;
    }

    public static IServiceCollection AddQdrantVectorStore(this IServiceCollection services)
    {
        services.AddHttpClient<IVectorStore, QdrantVectorStore>();
        return services;
    }

    public static IServiceCollection AddS3VectorsVectorStore(this IServiceCollection services)
    {
        services.AddAWSService<IAmazonS3Vectors>();
        services.AddScoped<IVectorStore, S3VectorStore>();
        return services;
    }
}
