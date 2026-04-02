using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public interface IPostsQueryService
{
    Task<List<Post>> GetAllPostsAsync();
    Task<Post?> GetPostByIdAsync(int id);
}
