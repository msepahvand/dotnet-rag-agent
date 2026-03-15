using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public interface ISemanticSearchService
{
    Task<List<SearchResult>> SearchAsync(string query, int topK);
}
