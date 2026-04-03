using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3;

namespace VectorSearch.IntegrationTests;

public class SemanticSearchPluginIntegrationTests
{
    [Fact]
    public async Task SearchPostsAsync_UsesWiredDependenciesAndReturnsGroundedSource()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AWS:EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
                ["AWS:ChatModelId"] = "anthropic.claude-3-5-sonnet-20250219-v1:0",
                ["VectorStore:Provider"] = "Qdrant"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVectorSearch(configuration);
        services.AddScoped<IVectorService, StubVectorService>();
        services.AddScoped<IPostService, StubPostService>();

        using var serviceProvider = services.BuildServiceProvider();
        var plugin = serviceProvider.GetRequiredService<SemanticSearchPlugin>();

        var result = await plugin.SearchPostsAsync("What is post one about?", 5);

        result.Should().Contain("\"PostId\":1");
        result.Should().Contain("\"Title\":\"Post 1\"");
        result.Should().Contain("deterministic body");
    }

    private sealed class StubVectorService : IVectorService
    {
        public Task EnsureInitializedAsync() => Task.CompletedTask;

        public Task<bool> IsIndexEmptyAsync() => Task.FromResult(false);

        public Task IndexPostAsync(Post post, float[] embedding) => Task.CompletedTask;

        public Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts) => Task.CompletedTask;

        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10)
        {
            var results = new List<SearchResult>
            {
                new() { PostId = 1, UserId = 10, Title = "Post 1", Distance = 0.08 }
            };

            return Task.FromResult(results);
        }
    }

    private sealed class StubPostService : IPostService
    {
        public Task<List<Post>> GetAllPostsAsync() => Task.FromResult(new List<Post>());

        public Task<Post?> GetPostByIdAsync(int id)
        {
            if (id != 1)
            {
                return Task.FromResult<Post?>(null);
            }

            var post = new Post(
                1,
                10,
                "Post 1",
                "This is a deterministic body used by SemanticSearchPlugin integration testing.");

            return Task.FromResult<Post?>(post);
        }
    }
}
