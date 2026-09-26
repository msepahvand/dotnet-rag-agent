using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
using Testcontainers.Qdrant;
using Testcontainers.Redis;
using RagAgent.Api;
using RagAgent.Api.Services;
using RagAgent.Core;
using RagAgent.Agents;
using RagAgent.Qdrant;
using RagAgent.Redis;

namespace RagAgent.IntegrationTests;

public class VectorSearchWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _provider;
    private readonly bool _useRealAgentPipeline;
    private readonly QdrantContainer? _qdrantContainer;
    private readonly RedisContainer? _redisContainer;

    public VectorSearchWebApplicationFactory(string provider, bool useRealAgentPipeline = false)
    {
        _provider = provider;
        _useRealAgentPipeline = useRealAgentPipeline;

        if (provider == "Qdrant")
        {
#pragma warning disable CS0618
            _qdrantContainer = new QdrantBuilder()
#pragma warning restore CS0618
                .WithImage("qdrant/qdrant:latest")
                .Build();
        }
        else if (provider == "Redis")
        {
#pragma warning disable CS0618
            _redisContainer = new RedisBuilder()
#pragma warning restore CS0618
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
                ["VectorStore:Provider"] = _provider,
                ["Swagger:Enabled"] = "true",
                ["Ingestion:Enabled"] = "false",
                ["Ingestion:IndexOnStartup"] = "false"
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
            if (!_useRealAgentPipeline)
            {
                services.RemoveAll<IAgentAnswerService>();
            }
            else
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(_ => new IntegrationTestChatClient(
                [
                    """{"answer":"Post 1 is about a test story.","citations":[{"postId":1,"quote":"This is deterministic content for post 1"}],"grounded":true}""",
                    """{"approved":true,"feedback":"","checks":["relevance: PASS","groundedness: PASS"]}"""
                ]));
            }

            // Remove and replace the IVectorStore implementation based on provider
            services.RemoveAll<IVectorStore>();

            if (_provider == "Qdrant")
            {
                services.AddHttpClient<IVectorStore, QdrantVectorStore>();
            }
            else if (_provider == "Redis")
            {
                services.AddScoped<IVectorStore, RedisVectorStore>();
            }

            // Replace the real embedding service with a mock for testing
            // This eliminates the need for AWS Bedrock credentials
            services.RemoveAll<IEmbeddingService>();
            services.AddScoped<IEmbeddingService, MockEmbeddingService>();

            // Replace external post source with deterministic test data
            // to keep integration tests stable regardless of appsettings data source.
            services.RemoveAll<IPostService>();
            services.AddScoped<IPostService, TestPostService>();

            if (!_useRealAgentPipeline)
            {
                // Replace grounded answer generation so ordinary endpoint tests do not
                // depend on Bedrock runtime services.
                services.AddScoped<IAgentAnswerService, TestAgentAnswerService>();
            }

            // Streaming endpoint tests use a deterministic stub.
            services.RemoveAll<IAgentStreamingService>();
            services.AddScoped<IAgentStreamingService, StubAgentStreamingService>();

            // Remove startup indexing — tests control index state themselves.
            var indexingService = services.FirstOrDefault(d => d.ImplementationType == typeof(IndexingStartupService));
            if (indexingService != null)
            {
                services.Remove(indexingService);
            }
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

            using var redisStore = new RedisVectorStore(config);
            await redisStore.CreateCollectionAsync(1024);
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
