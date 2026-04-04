using RagAgent.Api.Services.Contracts;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public interface IPostIndexingService
{
    Task<IndexAllPostsResult> IndexAllAsync();
    Task<IndexAllPostsResult> IndexPostsAsync(IReadOnlyList<Post> posts);
    Task<IndexSinglePostResult?> IndexSingleAsync(int id);
}
