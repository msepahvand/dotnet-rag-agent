using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IVectorService
{
    Task IndexPostAsync(Post post, float[] embedding);
    Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts);
    Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10);
    Task<bool> IsIndexEmptyAsync();
    Task EnsureInitializedAsync();
}
