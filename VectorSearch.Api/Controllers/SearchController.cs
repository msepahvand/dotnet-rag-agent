using Microsoft.AspNetCore.Mvc;
using VectorSearch.Core;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(IVectorService vectorService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int topK = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { Message = "Query cannot be empty" });
        }

        var results = await vectorService.SemanticSearchAsync(query, topK <= 0 ? 10 : topK);
        return Ok(results);
    }
}
