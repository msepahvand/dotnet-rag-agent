using VectorSearch.Api.Services.Contracts;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public interface IPostIndexingService
{
    Task<IndexAllPostsResult> IndexAllAsync();
    Task<IndexAllPostsResult> IndexPostsAsync(IReadOnlyList<Post> posts);
    Task<IndexSinglePostResult?> IndexSingleAsync(int id);
}
