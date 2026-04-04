using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public sealed class SemanticSearchService(IVectorService vectorService) : ISemanticSearchService
{
    public Task<List<SearchResult>> SearchAsync(string query, int topK)
    {
        return vectorService.SemanticSearchAsync(query, TopKNormaliser.Normalise(topK));
    }
}
