using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public interface ISemanticSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, int topK);
}
