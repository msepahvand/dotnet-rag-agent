using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IPostIndexingService
{
    Task<IndexAllPostsResult> IndexAllAsync();
    Task<IndexAllPostsResult> IndexPostsAsync(IReadOnlyList<Post> posts);
    Task<IndexSinglePostResult?> IndexSingleAsync(int id);
}
