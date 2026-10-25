using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace RagAgent.Agents;

public interface IBedrockGuardrailClient
{
    Task<ApplyGuardrailResponse> ApplyGuardrailAsync(
        ApplyGuardrailRequest request,
        CancellationToken cancellationToken);
}

public sealed class BedrockGuardrailClient(IAmazonBedrockRuntime bedrockRuntime) : IBedrockGuardrailClient
{
    public Task<ApplyGuardrailResponse> ApplyGuardrailAsync(
        ApplyGuardrailRequest request,
        CancellationToken cancellationToken) =>
        bedrockRuntime.ApplyGuardrailAsync(request, cancellationToken);
}
