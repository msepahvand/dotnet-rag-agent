using Microsoft.Extensions.Configuration;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
using VectorSearch.Core;

namespace VectorSearch.Redis;

public class RedisVectorStore : IVectorStore, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly SearchCommands _ft;
    private readonly string _indexName;

    public RedisVectorStore(IConfiguration configuration)
    {
        var connectionString = configuration["VectorStore:Redis:ConnectionString"] ?? "localhost:6379";
        _indexName = configuration["VectorStore:Redis:IndexName"] ?? "posts_idx";

        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
        _ft = _db.FT();
    }

    public async Task<bool> CollectionExistsAsync()
    {
        try
        {
            await _ft.InfoAsync(_indexName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsEmptyAsync()
    {
        if (!await CollectionExistsAsync())
        {
            return true;
        }

        var result = await _ft.SearchAsync(_indexName, new Query("*").Limit(0, 0));
        return result.TotalResults == 0;
    }

    public async Task CreateCollectionAsync(int vectorSize)
    {
        if (await CollectionExistsAsync())
        {
            return;
        }

        var schema = new Schema()
            .AddTextField("title")
            .AddTextField("body")
            .AddVectorField(
                "embedding",
                Schema.VectorField.VectorAlgo.HNSW,
                new Dictionary<string, object>
                {
                    ["TYPE"] = "FLOAT32",
                    ["DIM"] = vectorSize,
                    ["DISTANCE_METRIC"] = "COSINE"
                });

        await _ft.CreateAsync(_indexName, new FTCreateParams().On(IndexDataType.HASH), schema);
    }

    public async Task IndexDocumentAsync(string key, float[] embedding, Dictionary<string, string> metadata)
    {
        var redisKey = $"post:{key}";
        var embeddingBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray();

        var hashFields = new List<HashEntry>
        {
            new("embedding", embeddingBytes)
        };

        // Add metadata fields
        foreach (var (metaKey, metaValue) in metadata)
        {
            hashFields.Add(new HashEntry(metaKey, metaValue));
        }

        await _db.HashSetAsync(redisKey, hashFields.ToArray());
    }

    public async Task IndexDocumentsBatchAsync(List<(string Key, float[] Embedding, Dictionary<string, string> Metadata)> documents)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task>();

        foreach (var (key, embedding, metadata) in documents)
        {
            var redisKey = $"post:{key}";
            var embeddingBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray();

            var hashFields = new List<HashEntry>
            {
                new("embedding", embeddingBytes)
            };

            // Add metadata fields
            foreach (var (metaKey, metaValue) in metadata)
            {
                hashFields.Add(new HashEntry(metaKey, metaValue));
            }

            tasks.Add(batch.HashSetAsync(redisKey, hashFields.ToArray()));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10)
    {
        var embeddingBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(queryEmbedding.AsSpan()).ToArray();

        var query = new Query($"*=>[KNN {topK} @embedding $vec AS score]")
            .AddParam("vec", embeddingBytes)
            .SetSortBy("score")
            .Limit(0, topK)
            .Dialect(2);

        var result = await _ft.SearchAsync(_indexName, query);

        var results = new List<VectorSearchResult>();
        foreach (var doc in result.Documents)
        {
            var metadata = new Dictionary<string, string>();
            var score = 0.0;

            foreach (var prop in doc.GetProperties())
            {
                var key = prop.Key;
                var value = prop.Value.ToString();

                if (key == "score")
                {
                    score = double.Parse(value);
                }
                else if (key != "embedding") // Skip the embedding bytes
                {
                    metadata[key] = value;
                }
            }

            results.Add(new VectorSearchResult
            {
                Key = doc.Id.ToString().Replace("post:", ""),
                Score = score,
                Metadata = metadata
            });
        }

        return results;
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
