using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VectorSearch.Core;

namespace VectorSearch.S3;

public class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly string _collectionName;
    private readonly int _vectorSize;

    public QdrantVectorStore(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<QdrantVectorStore> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var qdrantUrl = configuration["VectorStore:Qdrant:Url"] ?? "http://localhost:6333";
        _httpClient.BaseAddress = new Uri(qdrantUrl);

        _collectionName = configuration["VectorStore:Qdrant:CollectionName"] ?? "posts";
        _vectorSize = int.Parse(configuration["VectorStore:Qdrant:VectorSize"] ?? "1024");
    }

    public async Task<bool> CollectionExistsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"/collections/{_collectionName}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check if Qdrant collection {CollectionName} exists", _collectionName);
            return false;
        }
    }

    public async Task<bool> IsEmptyAsync()
    {
        if (!await CollectionExistsAsync())
        {
            return true;
        }

        var response = await _httpClient.GetAsync($"/collections/{_collectionName}");
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(jsonResponse);

        if (!document.RootElement.TryGetProperty("result", out var resultElement))
        {
            return true;
        }

        var pointsCount = ReadCount(resultElement, "points_count");
        if (pointsCount.HasValue)
        {
            return pointsCount.Value == 0;
        }

        var vectorsCount = ReadCount(resultElement, "vectors_count");
        return vectorsCount.GetValueOrDefault() == 0;
    }

    public async Task CreateCollectionAsync(int vectorSize)
    {
        var payload = new
        {
            vectors = new
            {
                size = vectorSize,
                distance = "Cosine"
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PutAsync($"/collections/{_collectionName}", content);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Created Qdrant collection: {CollectionName}", _collectionName);
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
        var points = documents.Select((doc, index) => new
        {
            id = int.Parse(doc.Key), // Convert string key to int for Qdrant
            vector = doc.Embedding,
            payload = doc.Metadata
        }).ToList();

        var payload = new
        {
            points = points
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PutAsync($"/collections/{_collectionName}/points", content);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Indexed {Count} documents to Qdrant", documents.Count);
    }

    public async Task<List<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 10)
    {
        var payload = new
        {
            vector = queryEmbedding,
            limit = topK,
            with_payload = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync($"/collections/{_collectionName}/points/search", content);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<QdrantSearchResponse>(jsonResponse);

        return result?.result?.Select(r => new VectorSearchResult
        {
            Score = r.score,
            Key = r.id?.ToString() ?? "",
            Metadata = r.payload ?? new Dictionary<string, string>()
        }).ToList() ?? new List<VectorSearchResult>();
    }

    private class QdrantSearchResponse
    {
        public List<QdrantPoint>? result { get; set; }
    }

    private class QdrantPoint
    {
        public object? id { get; set; }
        public double score { get; set; }
        public Dictionary<string, string>? payload { get; set; }
    }

    private static long? ReadCount(JsonElement resultElement, string propertyName)
    {
        if (!resultElement.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var count) => count,
            _ => null
        };
    }
}
