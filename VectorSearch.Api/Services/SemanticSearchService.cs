using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public sealed class SemanticSearchService(IVectorService vectorService) : ISemanticSearchService
{
    public Task<List<SearchResult>> SearchAsync(string query, int topK)
    {
        return vectorService.SemanticSearchAsync(query, TopKNormaliser.Normalise(topK));
    }
}
