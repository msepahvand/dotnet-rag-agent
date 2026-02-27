using Amazon.S3Vectors;
using Amazon.S3Vectors.Model;
using Microsoft.Extensions.Configuration;
using VectorSearch.Core;

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

    public async Task IndexPostAsync(Post post, float[] embedding)
    {
        var metadata = new Dictionary<string, string>
        {
            ["title"] = post.Title,
            ["userId"] = post.UserId.ToString(),
            ["postId"] = post.Id.ToString()
        };

        await _vectorStore.IndexDocumentAsync(post.Id.ToString(), embedding, metadata);
    }

    public async Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts)
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

/// <summary>
/// AWS S3 Vectors implementation - for production use
/// </summary>
public class S3VectorStore : IVectorStore
{
    private readonly IAmazonS3Vectors _s3VectorsClient;
    private readonly string _vectorBucketName;
    private readonly string _indexName;

    public S3VectorStore(
        IAmazonS3Vectors s3VectorsClient,
        IConfiguration configuration)
    {
        _s3VectorsClient = s3VectorsClient;
        _vectorBucketName = configuration["AWS:VectorBucketName"] ?? "posts-semantic-search";
        _indexName = configuration["AWS:VectorIndexName"] ?? "posts-content-index";
    }

    public Task<bool> CollectionExistsAsync()
    {
        // S3 Vectors buckets are created manually via AWS Console/CLI
        return Task.FromResult(true);
    }

    public Task CreateCollectionAsync(int vectorSize)
    {
        // S3 Vectors indexes are created manually via AWS Console/CLI
        return Task.CompletedTask;
    }

    public async Task IndexDocumentAsync(string key, float[] embedding, Dictionary<string, string> metadata)
    {
        await IndexDocumentsBatchAsync(new List<(string, float[], Dictionary<string, string>)>
        {
            (key, embedding, metadata)
        });
    }

    public async Task IndexDocumentsBatchAsync(List<(string Key, float[] Embedding, Dictionary<string, string> Metadata)> documents)
    {
        var vectors = documents.Select(d => new PutInputVector
        {
            Key = d.Key,
            Data = new VectorData
            {
                Float32 = d.Embedding.ToList()
            },
            Metadata = new Amazon.Runtime.Documents.Document(
                d.Metadata.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new Amazon.Runtime.Documents.Document(kvp.Value)
                ))
        }).ToList();

        await _s3VectorsClient.PutVectorsAsync(new PutVectorsRequest
        {
            VectorBucketName = _vectorBucketName,
            IndexName = _indexName,
            Vectors = vectors
        });
    }

    public async Task<List<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10)
    {
        var request = new QueryVectorsRequest
        {
            VectorBucketName = _vectorBucketName,
            IndexName = _indexName,
            QueryVector = new VectorData
            {
                Float32 = queryEmbedding.ToList()
            },
            TopK = topK,
            ReturnMetadata = true,
            ReturnDistance = true
        };

        QueryVectorsResponse response = await _s3VectorsClient.QueryVectorsAsync(request);

        return response.Vectors.Select(v => new VectorSearchResult
        {
            Score = 1.0 - (v.Distance ?? 0), // Convert distance to similarity score
            Key = v.Key ?? "",
            Metadata = v.Metadata.AsDictionary().ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString() ?? ""
            )
        }).ToList();
    }
}
