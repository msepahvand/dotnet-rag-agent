using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.Qdrant;
using Testcontainers.Redis;
using VectorSearch.Api;
using VectorSearch.Core;
using VectorSearch.S3;

namespace VectorSearch.IntegrationTests;

public class VectorSearchWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _provider;
    private readonly QdrantContainer? _qdrantContainer;
    private readonly RedisContainer? _redisContainer;

    public VectorSearchWebApplicationFactory(string provider)
    {
        _provider = provider;
        
        if (provider == "Qdrant")
        {
            _qdrantContainer = new QdrantBuilder()
                .WithImage("qdrant/qdrant:latest")
                .Build();
        }
        else if (provider == "Redis")
        {
            _redisContainer = new RedisBuilder()
                .WithImage("redis/redis-stack:latest")
                .Build();
        }
    }

    public string ConnectionString => _provider switch
    {
        "Qdrant" => $"http://{_qdrantContainer!.Hostname}:{_qdrantContainer.GetMappedPublicPort(6333)}",
        "Redis" => _redisContainer!.GetConnectionString(),
        _ => throw new InvalidOperationException($"Unknown provider: {_provider}")
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for testing based on provider
            var configValues = new Dictionary<string, string?>
            {
                ["VectorStore:Provider"] = _provider
            };

            if (_provider == "Qdrant")
            {
                configValues["VectorStore:Qdrant:Url"] = ConnectionString;
                configValues["VectorStore:Qdrant:CollectionName"] = "test_posts";
                configValues["VectorStore:Qdrant:VectorSize"] = "1024";
            }
            else if (_provider == "Redis")
            {
                configValues["VectorStore:Redis:ConnectionString"] = ConnectionString;
                configValues["VectorStore:Redis:IndexName"] = "test_posts";
                configValues["VectorStore:Redis:VectorSize"] = "1024";
            }

            config.AddInMemoryCollection(configValues);
        });

        builder.ConfigureServices(services =>
        {
            // Remove all AWS-related services to avoid credential requirements
            services.RemoveAll<Amazon.BedrockRuntime.IAmazonBedrockRuntime>();
            services.RemoveAll<Amazon.S3Vectors.IAmazonS3Vectors>();
            services.RemoveAll<Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>();
            services.RemoveAll<Microsoft.SemanticKernel.Kernel>();
            services.RemoveAll<IAgentAnswerService>();
            
            // Remove and replace the IVectorStore implementation based on provider
            services.RemoveAll<IVectorStore>();
            
            if (_provider == "Qdrant")
            {
                services.AddHttpClient<IVectorStore, QdrantVectorStore>();
            }
            else if (_provider == "Redis")
            {
                // Use reflection to avoid hard dependency on VectorSearch.Redis
                var redisStoreType = Type.GetType("VectorSearch.Redis.RedisVectorStore, VectorSearch.Redis");
                if (redisStoreType != null)
                {
                    services.AddScoped(typeof(IVectorStore), redisStoreType);
                }
            }
            
            // Replace the real embedding service with a mock for testing
            // This eliminates the need for AWS Bedrock credentials
            services.RemoveAll<IEmbeddingService>();
            services.AddScoped<IEmbeddingService, MockEmbeddingService>();

            // Replace grounded answer generation with a test implementation
            // so agent endpoint tests do not depend on Bedrock runtime services.
            services.AddScoped<IAgentAnswerService, TestAgentAnswerService>();
        });

        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        if (_provider == "Qdrant")
        {
            await _qdrantContainer!.StartAsync();
            
            // Create the Qdrant collection manually since we skip app initialization in tests
            var httpClient = new HttpClient();
            var collectionUrl = $"{ConnectionString}/collections/test_posts";
            var createCollectionPayload = new
            {
                vectors = new { size = 1024, distance = "Cosine" }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(createCollectionPayload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await httpClient.PutAsync(collectionUrl, content);
        }
        else if (_provider == "Redis")
        {
            await _redisContainer!.StartAsync();
            
            // Create the Redis index manually since we skip app initialization in tests
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["VectorStore:Redis:ConnectionString"] = ConnectionString,
                    ["VectorStore:Redis:IndexName"] = "test_posts"
                })
                .Build();
            
            var redisStoreType = Type.GetType("VectorSearch.Redis.RedisVectorStore, VectorSearch.Redis");
            if (redisStoreType != null)
            {
                var redisStore = (IVectorStore)Activator.CreateInstance(redisStoreType, config)!;
                await redisStore.CreateCollectionAsync(1024);
                
                // Dispose if it implements IDisposable
                if (redisStore is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    public new async Task DisposeAsync()
    {
        if (_qdrantContainer != null)
        {
            await _qdrantContainer.DisposeAsync();
        }
        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
        await base.DisposeAsync();
    }
}
