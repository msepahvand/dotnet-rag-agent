using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IEmbeddingService
{
    IAsyncEnumerable<PostEmbedding> StreamEmbeddings(
        List<Post> posts,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default);

    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(string text);
}
