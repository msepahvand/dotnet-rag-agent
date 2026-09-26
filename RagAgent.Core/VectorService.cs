using RagAgent.Core.Models;

namespace RagAgent.Core;

/// <summary>
/// Orchestrates the configured IVectorStore (S3 Vectors, Qdrant, or Redis) behind
/// the provider-agnostic IVectorService contract.
/// </summary>
public class VectorService : IVectorService
{
    private const int MaximumSearchCandidates = 1000;

    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;

    public VectorService(IVectorStore vectorStore, IEmbeddingService embeddingService)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
    }

    public async Task EnsureInitializedAsync()
    {
        if (!await _vectorStore.CollectionExistsAsync())
        {
            await _vectorStore.CreateCollectionAsync(1024); // Cohere embed-english-v3 embedding dimensions
        }
    }

    public Task<bool> IsIndexEmptyAsync()
    {
        return _vectorStore.IsEmptyAsync();
    }

    public Task IndexPostAsync(Post post, IReadOnlyList<float[]> embeddings) =>
        IndexPostsBatchAsync(embeddings
            .Select((embedding, chunkIndex) => new PostEmbedding(post, chunkIndex, embedding))
            .ToList());

    public async Task IndexPostsBatchAsync(List<PostEmbedding> embeddings)
    {
        var documents = embeddings.Select(embedding => (
            Key: CreateChunkKey(embedding.Post.Id, embedding.ChunkIndex),
            Embedding: embedding.Embedding,
            Metadata: new Dictionary<string, string>
            {
                ["title"] = embedding.Post.Title,
                ["userId"] = embedding.Post.UserId.ToString(),
                ["postId"] = embedding.Post.Id.ToString(),
                ["chunkIndex"] = embedding.ChunkIndex.ToString()
            }
        )).ToList();

        await _vectorStore.IndexDocumentsBatchAsync(documents);
    }

    public async Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10)
    {
        float[] queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        var candidateLimit = Math.Min(topK, MaximumSearchCandidates);
        List<VectorSearchResult> results;
        List<SearchResult> distinctPosts;

        while (true)
        {
            results = await _vectorStore.SearchAsync(queryEmbedding, candidateLimit);
            distinctPosts = results
                .Select(ToSearchResult)
                .GroupBy(result => result.PostId)
                .Select(group => group.MinBy(result => result.Distance)!)
                .OrderBy(result => result.Distance)
                .Take(topK)
                .ToList();

            if (distinctPosts.Count >= topK
                || results.Count < candidateLimit
                || candidateLimit == MaximumSearchCandidates)
            {
                return distinctPosts;
            }

            candidateLimit = Math.Min(candidateLimit * 2, MaximumSearchCandidates);
        }
    }

    private static SearchResult ToSearchResult(VectorSearchResult result) =>
        new()
        {
            Distance = 1.0 - result.Score,
            Title = result.Metadata.GetValueOrDefault("title", ""),
            PostId = int.Parse(result.Metadata.GetValueOrDefault("postId", "0")),
            UserId = int.Parse(result.Metadata.GetValueOrDefault("userId", "0"))
        };

    private static string CreateChunkKey(int postId, int chunkIndex) => $"{postId}:{chunkIndex}";
}
