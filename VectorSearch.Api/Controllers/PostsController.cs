using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Dtos.Mappers;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostsQueryService postsQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var posts = await postsQueryService.GetAllPostsAsync();
        return Ok(posts.Select(PostMapper.ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetByIdAsync(int id)
    {
        var post = await postsQueryService.GetPostByIdAsync(id);
        return post != null ? Ok(PostMapper.ToDto(post)) : NotFound();
    }
}
