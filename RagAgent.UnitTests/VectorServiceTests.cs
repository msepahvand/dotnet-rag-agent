using FluentAssertions;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public class VectorServiceTests
{
    [Fact]
    public async Task IndexPostAsync_IndexesOneVectorPerPostChunkAsync()
    {
        var store = new CapturingVectorStore();
        var service = new VectorService(store, new StubEmbeddingService());
        var post = new Post(42, 7, "A post", "Body");

        await service.IndexPostAsync(post, [[0.1f], [0.2f], [0.3f]]);

        store.IndexedDocuments.Should().HaveCount(3);
        store.IndexedDocuments.Select(document => document.Key)
            .Should().Equal("42:0", "42:1", "42:2");
        store.IndexedDocuments.Should().OnlyContain(
            document => document.Metadata["postId"] == "42");
        store.IndexedDocuments.Select(document => document.Metadata["chunkIndex"])
            .Should().Equal("0", "1", "2");
    }

    [Fact]
    public async Task SemanticSearchAsync_DeduplicatesChunkHitsAndFindsTopDistinctPostsAsync()
    {
        var store = new CapturingVectorStore
        {
            Results =
            [
                SearchResultFor(postId: 1, score: 0.99),
                SearchResultFor(postId: 1, score: 0.98),
                SearchResultFor(postId: 2, score: 0.97),
                SearchResultFor(postId: 3, score: 0.96)
            ]
        };
        var service = new VectorService(store, new StubEmbeddingService());

        var results = await service.SemanticSearchAsync("question", topK: 2);

        results.Select(result => result.PostId).Should().Equal(1, 2);
        results[0].Distance.Should().BeApproximately(0.01, 0.0001);
        store.SearchLimits.Should().Equal(2, 4);
    }

    private static RagAgent.Core.VectorSearchResult SearchResultFor(int postId, double score) =>
        new()
        {
            Score = score,
            Metadata = new Dictionary<string, string>
            {
                ["postId"] = postId.ToString(),
                ["userId"] = "1",
                ["title"] = $"Post {postId}"
            }
        };

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public IAsyncEnumerable<PostEmbedding> StreamEmbeddings(
            List<Post> posts,
            int maxConcurrency = 3,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<float[]> GenerateEmbeddingAsync(string text) => Task.FromResult(new[] { 1f });

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(string text) =>
            Task.FromResult<IReadOnlyList<float[]>>([]);
    }

    private sealed class CapturingVectorStore : IVectorStore
    {
        public List<(string Key, float[] Embedding, Dictionary<string, string> Metadata)> IndexedDocuments { get; } = [];

        public List<RagAgent.Core.VectorSearchResult> Results { get; init; } = [];

        public List<int> SearchLimits { get; } = [];

        public Task IndexDocumentAsync(string key, float[] embedding, Dictionary<string, string> metadata)
        {
            IndexedDocuments.Add((key, embedding, metadata));
            return Task.CompletedTask;
        }

        public Task IndexDocumentsBatchAsync(
            List<(string Key, float[] Embedding, Dictionary<string, string> Metadata)> documents)
        {
            IndexedDocuments.AddRange(documents);
            return Task.CompletedTask;
        }

        public Task<List<RagAgent.Core.VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10)
        {
            SearchLimits.Add(topK);
            return Task.FromResult(Results.Take(topK).ToList());
        }

        public Task<bool> IsEmptyAsync() => Task.FromResult(IndexedDocuments.Count == 0);

        public Task<bool> CollectionExistsAsync() => Task.FromResult(true);

        public Task CreateCollectionAsync(int vectorSize) => Task.CompletedTask;
    }
}
