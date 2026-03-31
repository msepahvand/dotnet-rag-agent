using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Contracts.Responses;
using VectorSearch.Api.Dtos;
using VectorSearch.Api.Dtos.Mappers;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(IAgentOrchestrationService agentOrchestrationService) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new MessageResponse("Question cannot be empty"));
        }

        var result = await agentOrchestrationService.AskAsync(AgentMapper.ToModel(request));
        return Ok(AgentMapper.ToDto(result));
    }
}
