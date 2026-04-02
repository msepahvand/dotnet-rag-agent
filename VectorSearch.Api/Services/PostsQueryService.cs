using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

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
