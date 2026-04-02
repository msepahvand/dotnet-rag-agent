using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class SemanticSearchService(IVectorService vectorService) : ISemanticSearchService
{
    public Task<List<SearchResult>> SearchAsync(string query, int topK)
    {
        var normalizedTopK = topK <= 0 ? 10 : topK;
        return vectorService.SemanticSearchAsync(query, normalizedTopK);
    }
}
