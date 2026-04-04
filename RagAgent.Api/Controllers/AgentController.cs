using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Dtos;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Api.Services;
using RagAgent.Core;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(IAgentOrchestrationService agentOrchestrationService) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> AskAsync([FromBody] AskRequestDto request)
    {
        try
        {
            var result = await agentOrchestrationService.AskAsync(AgentMapper.ToModel(request));
            return Ok(AgentMapper.ToDto(result));
        }
        catch (GuardrailException ex)
        {
            return BadRequest(new { error = ex.Reason });
        }
    }
}
