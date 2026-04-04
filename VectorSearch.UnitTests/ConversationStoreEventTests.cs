using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using VectorSearch.Api.Services;
using VectorSearch.Core.Models;

namespace VectorSearch.UnitTests;

public class ConversationStoreEventTests
{
    private static InMemoryConversationStore CreateStore() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Collects exactly <paramref name="count"/> events then stops, avoiding infinite blocking.</summary>
    private static async Task<List<ConversationEvent>> TakeAsync(
        IAsyncEnumerable<ConversationEvent> source, int count)
    {
        var results = new List<ConversationEvent>();
        await foreach (var e in source)
        {
            results.Add(e);
            if (results.Count >= count)
            {
                break;
            }
        }

        return results;
    }

    [Fact]
    public async Task AppendAsync_EmitsMessageAppendedEvent()
    {
        var store = CreateStore();
        var eventStream = store.Subscribe(); // registers before append — channel buffers the event

        var message = new ChatMessage("user", "hello");
        await store.AppendAsync("conv-1", message);

        var events = await TakeAsync(eventStream, 1);

        events.Should().ContainSingle()
            .Which.Should().BeOfType<ConversationEvent.MessageAppended>()
            .Which.Should().BeEquivalentTo(new { ConversationId = "conv-1", Message = message });
    }

    [Fact]
    public async Task DeleteAsync_EmitsConversationDeletedEvent()
    {
        var store = CreateStore();
        await store.AppendAsync("conv-1", new ChatMessage("user", "hello"));

        var eventStream = store.Subscribe();
        await store.DeleteAsync("conv-1");

        var events = await TakeAsync(eventStream, 1);

        events.Should().ContainSingle()
            .Which.Should().BeOfType<ConversationEvent.ConversationDeleted>()
            .Which.ConversationId.Should().Be("conv-1");
    }

    [Fact]
    public async Task CacheEviction_EmitsConversationExpiredEvent()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new InMemoryConversationStore(cache);

        await store.AppendAsync("conv-1", new ChatMessage("user", "hello"));
        var eventStream = store.Subscribe();

        cache.Remove("conv-1"); // simulates TTL expiry

        var events = await TakeAsync(eventStream, 1);

        events.Should().ContainSingle()
            .Which.Should().BeOfType<ConversationEvent.ConversationExpired>()
            .Which.ConversationId.Should().Be("conv-1");
    }

    [Fact]
    public async Task Events_EmitInOrder_ForMultipleOperations()
    {
        var store = CreateStore();
        var eventStream = store.Subscribe();

        var msg1 = new ChatMessage("user", "first");
        var msg2 = new ChatMessage("assistant", "second");
        await store.AppendAsync("conv-1", msg1);
        await store.AppendAsync("conv-1", msg2);
        await store.DeleteAsync("conv-1");

        var events = await TakeAsync(eventStream, 3);

        events[0].Should().BeOfType<ConversationEvent.MessageAppended>()
            .Which.Message.Should().Be(msg1);
        events[1].Should().BeOfType<ConversationEvent.MessageAppended>()
            .Which.Message.Should().Be(msg2);
        events[2].Should().BeOfType<ConversationEvent.ConversationDeleted>();
    }

    [Fact]
    public async Task Events_AreIsolatedBetweenConversations()
    {
        var store = CreateStore();
        var eventStream = store.Subscribe();

        await store.AppendAsync("conv-a", new ChatMessage("user", "hello"));
        await store.AppendAsync("conv-b", new ChatMessage("user", "world"));
        await store.DeleteAsync("conv-a");

        var events = await TakeAsync(eventStream, 3);

        events.OfType<ConversationEvent.MessageAppended>()
            .Select(e => e.ConversationId)
            .Should().BeEquivalentTo(["conv-a", "conv-b"]);

        events.OfType<ConversationEvent.ConversationDeleted>()
            .Should().ContainSingle()
            .Which.ConversationId.Should().Be("conv-a");
    }

    // ── Message cap ─────────────────────────────────────────────────────────
    [Fact]
    public async Task AppendAsync_WhenMessagesExceedCap_RemovesOldestMessages()
    {
        var store = CreateStore();
        const string convId = "conv-trim";

        for (var i = 0; i < 42; i++)
        {
            await store.AppendAsync(convId, new ChatMessage("user", $"message-{i}"));
        }

        var history = await store.GetHistoryAsync(convId);

        history.Should().HaveCount(40);
        history[0].Content.Should().Be("message-2");   // first two trimmed
        history[^1].Content.Should().Be("message-41"); // last message retained
    }
}
