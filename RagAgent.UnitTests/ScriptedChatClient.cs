using Microsoft.Extensions.AI;

namespace RagAgent.UnitTests;

internal sealed class ScriptedChatClient(
    string responseContent,
    Exception? responseException = null,
    IReadOnlyList<string>? streamedChunks = null) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("ScriptedChatClient");

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        cancellationToken.ThrowIfCancellationRequested();

        if (responseException is not null)
        {
            return Task.FromException<ChatResponse>(responseException);
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseContent)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;

        foreach (var chunk in streamedChunks ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
