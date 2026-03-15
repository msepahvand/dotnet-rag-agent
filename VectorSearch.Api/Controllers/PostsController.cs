using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostsQueryService postsQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var posts = await postsQueryService.GetAllPostsAsync();
        return Ok(posts);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var post = await postsQueryService.GetPostByIdAsync(id);
        return post != null ? Ok(post) : NotFound();
    }
}
