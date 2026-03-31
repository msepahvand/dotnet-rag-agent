using System.Collections.Concurrent;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public sealed class InMemoryConversationStore : IConversationStore
{
    private const int MaxMessagesPerConversation = 40;

    private readonly ConcurrentDictionary<string, List<ChatMessage>> _store = new();

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string conversationId)
    {
        if (_store.TryGetValue(conversationId, out var messages))
        {
            lock (messages)
            {
                return Task.FromResult<IReadOnlyList<ChatMessage>>([.. messages]);
            }
        }

        return Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    }

    public Task AppendAsync(string conversationId, ChatMessage message)
    {
        var messages = _store.GetOrAdd(conversationId, _ => []);

        lock (messages)
        {
            messages.Add(message);

            if (messages.Count > MaxMessagesPerConversation)
            {
                messages.RemoveRange(0, messages.Count - MaxMessagesPerConversation);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string conversationId)
    {
        _store.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListConversationIdsAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _store.Keys]);
    }
}
