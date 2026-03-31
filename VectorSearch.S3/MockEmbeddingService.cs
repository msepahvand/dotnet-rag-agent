using System.Security.Cryptography;
using System.Text;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

/// <summary>
/// Mock embedding service for testing - generates deterministic fake embeddings
/// </summary>
public class MockEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;

    public MockEmbeddingService(int dimensions = 1024)
    {
        _dimensions = dimensions;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Generate deterministic embeddings based on text hash
        // Similar texts will have similar embeddings
        var embedding = GenerateDeterministicEmbedding(text);
        return Task.FromResult(embedding);
    }

    public async Task<List<(int PostId, float[] Embedding)>> GenerateEmbeddingsAsync(List<Post> posts)
    {
        var embeddings = new List<(int PostId, float[] Embedding)>();
        foreach (var post in posts)
        {
            var text = $"{post.Title} {post.Body}";
            var embedding = await GenerateEmbeddingAsync(text);
            embeddings.Add((post.Id, embedding));
        }
        return embeddings;
    }

    private float[] GenerateDeterministicEmbedding(string text)
    {
        // Create a deterministic but pseudo-random embedding based on the text
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        var random = new Random(BitConverter.ToInt32(hash, 0));

        var embedding = new float[_dimensions];
        
        // Generate normalized random vector
        double sum = 0;
        for (int i = 0; i < _dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1); // Range [-1, 1]
            sum += embedding[i] * embedding[i];
        }

        // Normalize to unit length (for cosine similarity)
        float norm = (float)Math.Sqrt(sum);
        for (int i = 0; i < _dimensions; i++)
        {
            embedding[i] /= norm;
        }

        return embedding;
    }
}
