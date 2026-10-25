using System.Runtime.CompilerServices;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

internal abstract class AgentAnswerServiceTestDouble : IAgentAnswerService
{
    public abstract Task<AgentAnswerResult> AnswerAsync(AgentAnswerRequest request);

    public virtual async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
