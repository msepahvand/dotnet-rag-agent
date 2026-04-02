using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IPostService
{
    Task<List<Post>> GetAllPostsAsync();
    Task<Post?> GetPostByIdAsync(int id);
}
