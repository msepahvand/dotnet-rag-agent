using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public interface IPostIndexingService
{
    Task<IndexAllPostsResult> IndexAllAsync();
    Task<IndexSinglePostResult?> IndexSingleAsync(int id);
}
