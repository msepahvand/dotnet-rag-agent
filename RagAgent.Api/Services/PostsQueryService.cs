using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public sealed class PostsQueryService(IPostService postService) : IPostsQueryService
{
    public Task<List<Post>> GetAllPostsAsync()
    {
        return postService.GetAllPostsAsync();
    }

    public Task<Post?> GetPostByIdAsync(int id)
    {
        return postService.GetPostByIdAsync(id);
    }
}
