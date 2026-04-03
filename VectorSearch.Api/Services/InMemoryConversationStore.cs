using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public sealed class InMemoryConversationStore(IMemoryCache cache) : IConversationStore
{
    private const int MaxMessagesPerConversation = 40;
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Unit> _conversationIds = new();
    private readonly List<ChannelWriter<ConversationEvent>> _subscribers = [];
    private readonly object _subscribersLock = new();

    public IAsyncEnumerable<ConversationEvent> Subscribe(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ConversationEvent>();
        lock (_subscribersLock)
        {
            _subscribers.Add(channel.Writer);
        }

        return ReadEventsAsync(channel.Reader, channel.Writer, cancellationToken);
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string conversationId)
    {
        var messages = cache.Get<List<ChatMessage>>(conversationId);
        return Task.FromResult<IReadOnlyList<ChatMessage>>(messages is null ? [] : [.. messages]);
    }

    public Task AppendAsync(string conversationId, ChatMessage message)
    {
        var messages = cache.GetOrCreate(conversationId, entry =>
        {
            entry.SlidingExpiration = ConversationTtl;
            entry.RegisterPostEvictionCallback(OnEviction);
            return new List<ChatMessage>();
        })!;

        messages.Add(message);

        if (messages.Count > MaxMessagesPerConversation)
        {
            messages.RemoveRange(0, messages.Count - MaxMessagesPerConversation);
        }

        _conversationIds.TryAdd(conversationId, default);
        Broadcast(new ConversationEvent.MessageAppended(conversationId, message));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string conversationId)
    {
        cache.Remove(conversationId);
        _conversationIds.TryRemove(conversationId, out _);
        Broadcast(new ConversationEvent.ConversationDeleted(conversationId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListConversationIdsAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _conversationIds.Keys]);
    }

    private void Broadcast(ConversationEvent evt)
    {
        lock (_subscribersLock)
        {
            foreach (var writer in _subscribers)
            {
                writer.TryWrite(evt);
            }
        }
    }

    private async IAsyncEnumerable<ConversationEvent> ReadEventsAsync(
        ChannelReader<ConversationEvent> reader,
        ChannelWriter<ConversationEvent> writer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(writer);
            }
        }
    }

    private void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        _conversationIds.TryRemove(key.ToString()!, out _);
        Broadcast(new ConversationEvent.ConversationExpired(key.ToString()!));
    }
}
