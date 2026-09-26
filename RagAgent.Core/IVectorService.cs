using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IVectorService
{
    Task IndexPostAsync(Post post, IReadOnlyList<float[]> embeddings);
    Task IndexPostsBatchAsync(List<PostEmbedding> embeddings);
    Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10);
    Task<bool> IsIndexEmptyAsync();
    Task EnsureInitializedAsync();
}
