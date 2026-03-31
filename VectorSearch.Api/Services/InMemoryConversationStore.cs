using System.Collections.Concurrent;
using LanguageExt;
using Microsoft.Extensions.Caching.Memory;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public sealed class InMemoryConversationStore(IMemoryCache cache) : IConversationStore
{
    private const int MaxMessagesPerConversation = 40;
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Unit> _conversationIds = new();

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
            messages.RemoveRange(0, messages.Count - MaxMessagesPerConversation);

        _conversationIds.TryAdd(conversationId, default);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string conversationId)
    {
        cache.Remove(conversationId);
        _conversationIds.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListConversationIdsAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _conversationIds.Keys]);
    }

    private void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        _conversationIds.TryRemove(key.ToString()!, out _);
    }
}
