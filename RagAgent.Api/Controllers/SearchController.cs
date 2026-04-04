using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Contracts.Responses;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Api.Services;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISemanticSearchService semanticSearchService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> SearchAsync([FromQuery] string query, [FromQuery] int topK = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new MessageResponse("Query cannot be empty"));
        }

        var results = await semanticSearchService.SearchAsync(query, topK);
        return Ok(results.Select(SearchMapper.ToDto));
    }
}
