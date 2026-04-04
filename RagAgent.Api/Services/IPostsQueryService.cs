using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public interface IPostsQueryService
{
    Task<List<Post>> GetAllPostsAsync();
    Task<Post?> GetPostByIdAsync(int id);
}
