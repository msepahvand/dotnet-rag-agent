using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IPostService
{
    Task<List<Post>> GetAllPostsAsync();
    Task<Post?> GetPostByIdAsync(int id);
}
