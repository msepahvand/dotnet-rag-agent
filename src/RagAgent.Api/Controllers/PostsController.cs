using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Core;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostService postService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var posts = await postService.GetAllPostsAsync();
        return Ok(posts.Select(PostMapper.ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetByIdAsync(int id)
    {
        var post = await postService.GetPostByIdAsync(id);
        return post != null ? Ok(PostMapper.ToDto(post)) : NotFound();
    }
}
