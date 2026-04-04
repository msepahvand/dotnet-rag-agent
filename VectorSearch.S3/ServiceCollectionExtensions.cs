using Amazon.BedrockRuntime;
using Amazon.S3Vectors;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using VectorSearch.Core;
using VectorSearch.S3.Agents;
using VectorSearch.S3.Process;

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
        // Cohere Embed v3 uses a different request schema from Titan, so we use
        // a custom generator rather than the SK connector's BedrockEmbeddingGenerator.
        services.AddScoped<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            new CohereEmbeddingGenerator(
                sp.GetRequiredService<IAmazonBedrockRuntime>(),
                options.EmbeddingModelId));
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

        // Multi-agent: researcher retrieves, writer synthesises, critic reflects
        services.AddScoped<IResearcherAgent, ResearcherAgent>();
        services.AddScoped<IWriterAgent, WriterAgent>();
        services.AddScoped<ICriticAgent, CriticAgent>();

        // SK Process orchestration: bridges the process result back to request/response
        services.AddScoped<ProcessResultHolder>();
        services.AddScoped<IAgentAnswerService, ProcessAnswerService>();

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
