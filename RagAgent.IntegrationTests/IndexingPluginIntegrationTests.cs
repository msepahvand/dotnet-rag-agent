using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;
using RagAgent.Core.Models;
using RagAgent.Agents;

namespace RagAgent.IntegrationTests;

public class IndexingPluginIntegrationTests
{
    [Fact]
    public async Task IndexPostsIfEmptyAsync_IndexesPosts_WhenVectorStoreIsEmptyAsync()
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
    public async Task IndexPostsIfEmptyAsync_SkipsIndexing_WhenVectorStoreAlreadyHasPostsAsync()
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

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(string text) =>
            Task.FromResult<IReadOnlyList<float[]>>([new[] { 0.1f, 0.2f, 0.3f }]);

        public async IAsyncEnumerable<PostEmbedding> StreamEmbeddings(
            List<Post> posts, int maxConcurrency = 3, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var post in posts)
            {
                yield return new PostEmbedding(post, 0, new[] { 0.1f, 0.2f, 0.3f });
            }

            await Task.CompletedTask;
        }
    }

    private sealed class EmptyVectorService(bool isEmpty = true) : IVectorService
    {
        public bool IsEmpty { get; private set; } = isEmpty;
        public List<PostEmbedding> IndexedPosts { get; } = [];

        public Task EnsureInitializedAsync() => Task.CompletedTask;

        public Task<bool> IsIndexEmptyAsync() => Task.FromResult(IsEmpty);

        public Task IndexPostAsync(Post post, IReadOnlyList<float[]> embeddings)
        {
            IndexedPosts.AddRange(embeddings.Select((embedding, chunkIndex) =>
                new PostEmbedding(post, chunkIndex, embedding)));
            IsEmpty = false;
            return Task.CompletedTask;
        }

        public Task IndexPostsBatchAsync(List<PostEmbedding> embeddings)
        {
            IndexedPosts.AddRange(embeddings);
            IsEmpty = false;
            return Task.CompletedTask;
        }

        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10) => Task.FromResult(new List<SearchResult>());
    }
}
