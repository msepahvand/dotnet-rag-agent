using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IEmbeddingService
{
    IAsyncEnumerable<(int PostId, float[] Embedding)> StreamEmbeddings(List<Post> posts, int maxConcurrency = 3, CancellationToken cancellationToken = default);
    Task<float[]> GenerateEmbeddingAsync(string text);
}
