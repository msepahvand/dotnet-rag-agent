using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Contracts.Responses;
using VectorSearch.Api.Dtos.Mappers;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISemanticSearchService semanticSearchService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] int topK = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new MessageResponse("Query cannot be empty"));
        }

        var results = await semanticSearchService.SearchAsync(query, topK);
        return Ok(results.Select(SearchMapper.ToDto));
    }
}
