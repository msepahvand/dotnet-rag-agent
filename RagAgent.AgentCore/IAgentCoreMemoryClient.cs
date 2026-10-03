using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;

namespace RagAgent.AgentCore;

public interface IAgentCoreMemoryClient
{
    Task<CreateEventResponse> CreateEventAsync(CreateEventRequest request, CancellationToken cancellationToken);

    Task<ListEventsResponse> ListEventsAsync(ListEventsRequest request, CancellationToken cancellationToken);

    Task<DeleteEventResponse> DeleteEventAsync(DeleteEventRequest request, CancellationToken cancellationToken);

    Task<ListSessionsResponse> ListSessionsAsync(ListSessionsRequest request, CancellationToken cancellationToken);
}

public sealed class AgentCoreMemoryClient(IAmazonBedrockAgentCore client) : IAgentCoreMemoryClient
{
    public Task<CreateEventResponse> CreateEventAsync(
        CreateEventRequest request,
        CancellationToken cancellationToken) =>
        client.CreateEventAsync(request, cancellationToken);

    public Task<ListEventsResponse> ListEventsAsync(
        ListEventsRequest request,
        CancellationToken cancellationToken) =>
        client.ListEventsAsync(request, cancellationToken);

    public Task<DeleteEventResponse> DeleteEventAsync(
        DeleteEventRequest request,
        CancellationToken cancellationToken) =>
        client.DeleteEventAsync(request, cancellationToken);

    public Task<ListSessionsResponse> ListSessionsAsync(
        ListSessionsRequest request,
        CancellationToken cancellationToken) =>
        client.ListSessionsAsync(request, cancellationToken);
}
