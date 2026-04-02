using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IVectorService
{
    Task IndexPostAsync(Post post, float[] embedding);
    Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts);
    Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10);
    Task<bool> IsIndexEmptyAsync();
    Task EnsureInitializedAsync();
}

public record SearchResult
{
    public double Distance { get; init; }
    public string Title { get; init; } = string.Empty;
    public int PostId { get; init; }
    public int UserId { get; init; }
}
