using RagAgent.Api.Dtos;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public interface IAgentStreamingService
{
    /// <summary>
    /// Runs the agent pipeline and yields SSE events: status updates, retrieved sources,
    /// individual LLM tokens, and a final done event.
    /// </summary>
    IAsyncEnumerable<StreamEventDto> StreamAsync(AgentAskRequest request, CancellationToken ct = default);
}
