using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Dtos;
using VectorSearch.Api.Dtos.Mappers;
using VectorSearch.Api.Services;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(IAgentOrchestrationService agentOrchestrationService) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> AskAsync([FromBody] AskRequestDto request)
    {
        var result = await agentOrchestrationService.AskAsync(AgentMapper.ToModel(request));
        return Ok(AgentMapper.ToDto(result));
    }
}
