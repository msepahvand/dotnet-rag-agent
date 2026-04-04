using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Contracts.Responses;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Api.Services;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/index")]
public sealed class IndexController(IPostIndexingService postIndexingService) : ControllerBase
{
    [HttpPost("all")]
    public async Task<IActionResult> IndexAllAsync()
    {
        var result = await postIndexingService.IndexAllAsync();
        return Ok(new IndexAllPostsResponse($"Indexed {result.Count} posts successfully", result.Count));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> IndexSingleAsync(int id)
    {
        var result = await postIndexingService.IndexSingleAsync(id);
        if (result == null)
        {
            return NotFound(new MessageResponse($"Post {id} not found"));
        }

        return Ok(new IndexSinglePostResponse($"Indexed post {id} successfully", PostMapper.ToDto(result.Post)));
    }
}
