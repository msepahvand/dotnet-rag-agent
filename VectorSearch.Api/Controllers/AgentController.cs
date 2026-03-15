using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Contracts.Responses;
using VectorSearch.Api.Services;
using VectorSearch.Core;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(IAgentOrchestrationService agentOrchestrationService) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AgentAskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new MessageResponse("Question cannot be empty"));
        }

        var response = await agentOrchestrationService.AskAsync(request);
        return Ok(response);
    }
}
