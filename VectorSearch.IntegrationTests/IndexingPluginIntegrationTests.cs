using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3;

namespace VectorSearch.IntegrationTests;

public class IndexingPluginIntegrationTests
{
    [Fact]
    public async Task IndexPostsIfEmptyAsync_IndexesPosts_WhenVectorStoreIsEmpty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPostService, StubPostService>();
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();
        services.AddScoped<IVectorService, EmptyVectorService>();
        services.AddScoped<IndexingPlugin>();

        using var provider = services.BuildServiceProvider();
        var plugin = provider.GetRequiredService<IndexingPlugin>();
        var vectorService = (EmptyVectorService)provider.GetRequiredService<IVectorService>();

        var result = await plugin.IndexPostsIfEmptyAsync();

        result.Should().Contain("Indexed 2 posts");
        vectorService.IndexedPosts.Should().HaveCount(2);
        vectorService.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task IndexPostsIfEmptyAsync_SkipsIndexing_WhenVectorStoreAlreadyHasPosts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPostService, StubPostService>();
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();
        services.AddScoped<IVectorService>(_ => new EmptyVectorService(isEmpty: false));
        services.AddScoped<IndexingPlugin>();

        using var provider = services.BuildServiceProvider();
        var plugin = provider.GetRequiredService<IndexingPlugin>();
        var vectorService = (EmptyVectorService)provider.GetRequiredService<IVectorService>();

        var result = await plugin.IndexPostsIfEmptyAsync();

        result.Should().Be("Vector index already contains posts.");
        vectorService.IndexedPosts.Should().BeEmpty();
    }

    private sealed class StubPostService : IPostService
    {
        public Task<List<Post>> GetAllPostsAsync() => Task.FromResult(new List<Post>
        {
            new(1, 0, "RAG overview", "Retrieval augmented generation basics."),
            new(2, 0, "Latest vector database release", "Release notes for a vector database.")
        });

        public Task<Post?> GetPostByIdAsync(int id) => Task.FromResult<Post?>(null);
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text) => Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });

        public Task<List<(int PostId, float[] Embedding)>> GenerateEmbeddingsAsync(List<Post> posts) =>
            Task.FromResult(posts.Select(post => (post.Id, new[] { 0.1f, 0.2f, 0.3f })).ToList());
    }

    private sealed class EmptyVectorService(bool isEmpty = true) : IVectorService
    {
        public bool IsEmpty { get; private set; } = isEmpty;
        public List<(Post Post, float[] Embedding)> IndexedPosts { get; } = [];

        public Task EnsureInitializedAsync() => Task.CompletedTask;

        public Task<bool> IsIndexEmptyAsync() => Task.FromResult(IsEmpty);

        public Task IndexPostAsync(Post post, float[] embedding)
        {
            IndexedPosts.Add((post, embedding));
            IsEmpty = false;
            return Task.CompletedTask;
        }

        public Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts)
        {
            IndexedPosts.AddRange(posts);
            IsEmpty = false;
            return Task.CompletedTask;
        }

        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10) => Task.FromResult(new List<SearchResult>());
    }
}