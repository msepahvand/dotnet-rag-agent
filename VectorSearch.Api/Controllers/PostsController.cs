using Microsoft.AspNetCore.Mvc;
using VectorSearch.Core;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostService postService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var posts = await postService.GetAllPostsAsync();
        return Ok(posts);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var post = await postService.GetPostByIdAsync(id);
        return post != null ? Ok(post) : NotFound();
    }
}
