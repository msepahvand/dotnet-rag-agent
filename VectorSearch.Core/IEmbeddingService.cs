namespace VectorSearch.Core;

public interface IEmbeddingService
{
    Task<List<(int PostId, float[] Embedding)>> GenerateEmbeddingsAsync(List<Post> posts);
    Task<float[]> GenerateEmbeddingAsync(string text);
}
