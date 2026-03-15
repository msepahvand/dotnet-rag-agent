using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Contracts.Responses;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/index")]
public sealed class IndexController(IPostIndexingService postIndexingService) : ControllerBase
{
    [HttpPost("all")]
    public async Task<IActionResult> IndexAll()
    {
        var result = await postIndexingService.IndexAllAsync();
        return Ok(new IndexAllPostsResponse($"Indexed {result.Count} posts successfully", result.Count));
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> IndexSingle(int id)
    {
        var result = await postIndexingService.IndexSingleAsync(id);
        if (result == null)
        {
            return NotFound(new MessageResponse($"Post {id} not found"));
        }

        return Ok(new IndexSinglePostResponse($"Indexed post {id} successfully", result.Post));
    }
}
