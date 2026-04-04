using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Dtos;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Api.Services;
using RagAgent.Core;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(
    IAgentOrchestrationService agentOrchestrationService,
    IAgentStreamingService agentStreamingService) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Batch endpoint ────────────────────────────────────────────────────────
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

    // ── Streaming endpoint ────────────────────────────────────────────────────

    /// <summary>
    /// Streams the agent response as Server-Sent Events (SSE).
    /// Event types: status, sources, token, done, error.
    /// </summary>
    [HttpPost("ask/stream")]
    public async Task StreamAsync([FromBody] AskRequestDto request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx / ALB response buffering

        try
        {
            await foreach (var evt in agentStreamingService.StreamAsync(AgentMapper.ToModel(request), ct))
            {
                var json = JsonSerializer.Serialize(evt, SseJsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — normal, no action needed.
        }
    }
}
