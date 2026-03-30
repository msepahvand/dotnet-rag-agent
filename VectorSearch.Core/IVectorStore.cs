namespace VectorSearch.Core;

/// <summary>
/// Abstraction for vector storage - supports multiple providers (S3 Vectors, Qdrant, Redis, etc.)
/// </summary>
public interface IVectorStore
{
    Task IndexDocumentAsync(string key, float[] embedding, Dictionary<string, string> metadata);
    Task IndexDocumentsBatchAsync(List<(string Key, float[] Embedding, Dictionary<string, string> Metadata)> documents);
    Task<List<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10);
    Task<bool> IsEmptyAsync();
    Task<bool> CollectionExistsAsync();
    Task CreateCollectionAsync(int vectorSize);
}

public record VectorSearchResult
{
    public double Score { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public string Key { get; init; } = string.Empty;
}
