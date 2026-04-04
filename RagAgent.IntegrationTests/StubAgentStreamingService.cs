using RagAgent.Api.Dtos;
using RagAgent.Api.Services;
using RagAgent.Core.Models;

namespace RagAgent.IntegrationTests;

/// <summary>
/// No-op streaming service used in integration tests to avoid resolving AWS/SK dependencies.
/// </summary>
internal sealed class StubAgentStreamingService : IAgentStreamingService
{
    public async IAsyncEnumerable<StreamEventDto> StreamAsync(
        AgentAskRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
