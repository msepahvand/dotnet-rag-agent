using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IEmbeddingService
{
    IAsyncEnumerable<(int PostId, float[] Embedding)> StreamEmbeddings(List<Post> posts, int maxConcurrency = 3, CancellationToken cancellationToken = default);
    Task<float[]> GenerateEmbeddingAsync(string text);

    async Task<List<(int PostId, float[] Embedding)>> GenerateEmbeddingsAsync(List<Post> posts)
    {
        var results = new List<(int PostId, float[] Embedding)>();
        await foreach (var item in StreamEmbeddings(posts))
            results.Add(item);
        return results;
    }
}
