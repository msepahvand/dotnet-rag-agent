using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RagAgent.IntegrationTests;

internal sealed class IntegrationTestChatClient(IEnumerable<ChatResponse> responses) : IChatClient
{
    private readonly ConcurrentQueue<ChatResponse> _responses = new(responses);

    public ChatClientMetadata Metadata { get; } = new("IntegrationTestChatClient");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException("No scripted chat response remains for this integration test.");
        }

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
