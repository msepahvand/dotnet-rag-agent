using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

/// <summary>
/// Vector service that uses the configured IVectorStore (S3 Vectors, Qdrant, or Redis)
/// </summary>
public class VectorService : IVectorService
{
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
            await _vectorStore.CreateCollectionAsync(1024); // Titan embedding dimensions
        }
    }

    public Task<bool> IsIndexEmptyAsync()
    {
        return _vectorStore.IsEmptyAsync();
    }

    public async Task IndexPostAsync(Core.Models.Post post, float[] embedding)
    {
        var metadata = new Dictionary<string, string>
        {
            ["title"] = post.Title,
            ["userId"] = post.UserId.ToString(),
            ["postId"] = post.Id.ToString()
        };

        await _vectorStore.IndexDocumentAsync(post.Id.ToString(), embedding, metadata);
    }

    public async Task IndexPostsBatchAsync(List<(Core.Models.Post Post, float[] Embedding)> posts)
    {
        var documents = posts.Select(p => (
            Key: p.Post.Id.ToString(),
            Embedding: p.Embedding,
            Metadata: new Dictionary<string, string>
            {
                ["title"] = p.Post.Title,
                ["userId"] = p.Post.UserId.ToString(),
                ["postId"] = p.Post.Id.ToString()
            }
        )).ToList();

        await _vectorStore.IndexDocumentsBatchAsync(documents);
    }

    public async Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10)
    {
        float[] queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        var results = await _vectorStore.SearchAsync(queryEmbedding, topK);

        return results.Select(r => new SearchResult
        {
            Distance = 1.0 - r.Score, // Convert similarity to distance
            Title = r.Metadata.GetValueOrDefault("title", ""),
            PostId = int.Parse(r.Metadata.GetValueOrDefault("postId", "0")),
            UserId = int.Parse(r.Metadata.GetValueOrDefault("userId", "0"))
        }).ToList();
    }
}
