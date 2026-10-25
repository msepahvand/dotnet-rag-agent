using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IAgentAnswerService
{
    Task<AgentAnswerResult> AnswerAsync(AgentAnswerRequest request);

    IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentAnswerRequest request, CancellationToken cancellationToken = default);
}
