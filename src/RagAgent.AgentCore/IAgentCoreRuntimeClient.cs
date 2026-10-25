using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;

namespace RagAgent.AgentCore;

public interface IAgentCoreRuntimeClient
{
    Task<InvokeAgentRuntimeResponse> InvokeAsync(
        InvokeAgentRuntimeRequest request,
        CancellationToken cancellationToken);
}

public sealed class AgentCoreRuntimeClient(IAmazonBedrockAgentCore client) : IAgentCoreRuntimeClient
{
    public Task<InvokeAgentRuntimeResponse> InvokeAsync(
        InvokeAgentRuntimeRequest request,
        CancellationToken cancellationToken) =>
        client.InvokeAgentRuntimeAsync(request, cancellationToken);
}
