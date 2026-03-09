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
        var chatModelId = configuration["AWS:ChatModelId"] ?? "anthropic.claude-3-haiku-20240307-v1:0";

        // Register HttpClient for JSONPlaceholder
        services.AddHttpClient<IPostService, JsonPlaceholderService>();

        // Register vector store based on configuration
        var vectorProvider = configuration["VectorStore:Provider"] ?? "S3Vectors";
        
        if (vectorProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase))
        {
            // Use Qdrant for local development/testing
            services.AddHttpClient<IVectorStore, QdrantVectorStore>();
            
            // Configure Semantic Kernel with Bedrock embeddings
            services.AddBedrockEmbeddingGenerator(
                configuration["AWS:EmbeddingModelId"] ?? "amazon.titan-embed-text-v2:0");
            services.AddBedrockChatCompletionService(chatModelId);
            
            // Register Kernel
            services.AddTransient(sp => new Kernel(sp));
            
            // Register embedding service
            services.AddScoped<IEmbeddingService, EmbeddingService>();
        }
        else if (vectorProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            // Use Redis Stack for local development/testing
            // Note: Requires VectorSearch.Redis project reference
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
            
            // Configure Semantic Kernel with Bedrock embeddings
            services.AddBedrockEmbeddingGenerator(
                configuration["AWS:EmbeddingModelId"] ?? "amazon.titan-embed-text-v2:0");
            services.AddBedrockChatCompletionService(chatModelId);
            
            // Register Kernel
            services.AddTransient(sp => new Kernel(sp));
            
            // Register embedding service
            services.AddScoped<IEmbeddingService, EmbeddingService>();
        }
        else
        {
            // Use AWS S3 Vectors for production
            
            // Configure Semantic Kernel with Bedrock embeddings
            services.AddBedrockEmbeddingGenerator(
                configuration["AWS:EmbeddingModelId"] ?? "amazon.titan-embed-text-v2:0");
            services.AddBedrockChatCompletionService(chatModelId);
            
            // Register Kernel
            services.AddTransient(sp => new Kernel(sp));
            
            // Register embedding service
            services.AddScoped<IEmbeddingService, EmbeddingService>();
            
            services.AddAWSService<IAmazonBedrockRuntime>();
            services.AddAWSService<IAmazonS3Vectors>();
            services.AddScoped<IVectorStore, S3VectorStore>();
        }

        // Register the main vector service
        services.AddScoped<IVectorService, VectorService>();

        return services;
    }
}
