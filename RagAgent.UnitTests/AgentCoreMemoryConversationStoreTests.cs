using Amazon.BedrockAgentCore.Model;
using Amazon.Runtime.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RagAgent.AgentCore;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public class AgentCoreMemoryConversationStoreTests
{
    [Theory]
    [InlineData("conv-xyz")]
    [InlineData("spaces and:colons")]
    [InlineData("hash#comma,")]
    [InlineData("conversation 😀")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task AppendAndGetHistoryAsync_PreservesArbitraryConversationIdsAsync(string conversationId)
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);

        await sut.AppendAsync(conversationId, new ConversationMessage("user", "hello"));
        var history = await sut.GetHistoryAsync(conversationId);

        history.Should().ContainSingle().Which.Content.Should().Be("hello");
        client.CreateRequests.Should().HaveCount(2);
        client.CreateRequests[0].SessionId.Should().Be(AgentCoreMemoryConversationStore.GetSessionId(conversationId));
        client.CreateRequests[0].SessionId.Should().MatchRegex("^[a-f0-9]{64}$");
        client.CreateRequests[0].Payload.Should().HaveCount(2);
        client.CreateRequests[0].Payload[1].Json.Content.AsDictionary()["ConversationId"].AsString().Should().Be(conversationId);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyMostRecentFortyMessagesInTimestampOrderAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);

        for (var index = 0; index < 42; index++)
        {
            await sut.AppendAsync("conversation", new ConversationMessage("user", $"message-{index}"));
        }

        var history = await sut.GetHistoryAsync("conversation");

        history.Should().HaveCount(40);
        history[0].Content.Should().Be("message-2");
        history[^1].Content.Should().Be("message-41");
        client.Events.Count(evt => evt.Payload?.Any(payload => payload.Conversational is not null) == true)
            .Should().Be(40);
        client.DeleteRequests.Should().HaveCount(2);
        client.LastListEventsRequest!.MaxResults.Should().Be(100);
        client.LastListEventsRequest.IncludePayloads.Should().BeTrue();
    }

    [Fact]
    public async Task GetHistoryAsync_RenewsSlidingExpirationWithoutStoringActivityAsMessagesAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);
        await sut.AppendAsync("conversation", new ConversationMessage("user", "hello"));
        client.AgeMessageEvents(AgentCoreMemoryConversationStore.GetSessionId("conversation"), TimeSpan.FromMinutes(29));

        var history = await sut.GetHistoryAsync("conversation");

        history.Should().ContainSingle().Which.Content.Should().Be("hello");
        client.Events
            .Where(evt =>
                evt.Payload?.Any(payload => payload.Conversational is not null) != true &&
                evt.Payload?.Any(payload => payload.Json is not null) == true)
            .Should()
            .ContainSingle();

        client.AgeMessageEvents(AgentCoreMemoryConversationStore.GetSessionId("conversation"), TimeSpan.FromMinutes(31));
        var renewedHistory = await sut.GetHistoryAsync("conversation");

        renewedHistory.Should().ContainSingle().Which.Content.Should().Be("hello");
        client.DeleteRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task AppendAsync_RemovesActivityEventsAndRetainsMessageHistoryAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);
        await sut.AppendAsync("conversation", new ConversationMessage("user", "hello"));
        await sut.GetHistoryAsync("conversation");

        await sut.AppendAsync("conversation", new ConversationMessage("assistant", "reply"));

        client.Events.Should().HaveCount(2);
        client.Events
            .Where(evt => evt.Payload?.Any(payload => payload.Conversational is not null) == true)
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenConversationIsOlderThanThirtyMinutes_DeletesEventsAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);
        await sut.AppendAsync("conversation", new ConversationMessage("user", "stale"));
        client.AgeEvents(AgentCoreMemoryConversationStore.GetSessionId("conversation"), TimeSpan.FromMinutes(31));

        var history = await sut.GetHistoryAsync("conversation");

        history.Should().BeEmpty();
        client.Events.Should().BeEmpty();
        client.DeleteRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task AppendAsync_WhenConversationIsExpired_RemovesOldMessagesBeforeAppendingAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);
        await sut.AppendAsync("conversation", new ConversationMessage("user", "stale"));
        client.AgeEvents(AgentCoreMemoryConversationStore.GetSessionId("conversation"), TimeSpan.FromMinutes(31));

        await sut.AppendAsync("conversation", new ConversationMessage("user", "fresh"));
        var history = await sut.GetHistoryAsync("conversation");

        history.Should().ContainSingle().Which.Content.Should().Be("fresh");
        client.DeleteRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task ListConversationIdsAsync_ExcludesExpiredSessionsAndReturnsOriginalIdsAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        var sut = CreateStore(client);
        await sut.AppendAsync("recent #1", new ConversationMessage("user", "fresh"));
        await sut.AppendAsync("stale", new ConversationMessage("user", "old"));
        client.AgeEvents(AgentCoreMemoryConversationStore.GetSessionId("stale"), TimeSpan.FromMinutes(31));

        var conversationIds = await sut.ListConversationIdsAsync();

        conversationIds.Should().Equal("recent #1");
        client.LastListSessionsRequest!.MaxResults.Should().Be(100);
    }

    [Fact]
    public async Task DeleteAsync_DeletesEventsAcrossMultiplePagesAsync()
    {
        var client = new FakeAgentCoreMemoryClient();
        client.SeedEvents("conversation", 105);
        var sut = CreateStore(client);

        await sut.DeleteAsync("conversation");

        client.Events.Should().BeEmpty();
        client.DeleteRequests.Should().HaveCount(105);
    }

    [Fact]
    public async Task GetHistoryAsync_WhenAgentCoreRejectsRequest_MapsValidationFailureAsync()
    {
        var client = new FakeAgentCoreMemoryClient
        {
            ListEventsFailure = new ValidationException("bad request")
        };
        var sut = CreateStore(client);

        var act = () => sut.GetHistoryAsync("conversation");

        await act.Should().ThrowAsync<ConversationStoreValidationException>()
            .WithMessage("The conversation request is invalid.");
    }

    [Fact]
    public async Task ListConversationIdsAsync_WhenMemoryResourceIsMissing_MapsNotFoundFailureAsync()
    {
        var client = new FakeAgentCoreMemoryClient
        {
            ListSessionsFailure = new ResourceNotFoundException("not found")
        };
        var sut = CreateStore(client);

        var act = () => sut.ListConversationIdsAsync();

        await act.Should().ThrowAsync<ConversationStoreNotFoundException>()
            .WithMessage("The conversation store was not found.");
    }

    [Fact]
    public async Task GetHistoryAsync_WhenSessionDoesNotExist_ReturnsEmptyHistoryAsync()
    {
        var client = new FakeAgentCoreMemoryClient
        {
            ListEventsFailure = new ResourceNotFoundException("session not found")
        };
        var sut = CreateStore(client);

        var history = await sut.GetHistoryAsync("conversation");

        history.Should().BeEmpty();
    }

    private static AgentCoreMemoryConversationStore CreateStore(FakeAgentCoreMemoryClient client) =>
        new(client, new AgentCoreMemoryOptions("memory-id"), NullLogger<AgentCoreMemoryConversationStore>.Instance);

    private sealed class FakeAgentCoreMemoryClient : IAgentCoreMemoryClient
    {
        public List<Event> Events { get; } = [];

        public List<CreateEventRequest> CreateRequests { get; } = [];

        public List<DeleteEventRequest> DeleteRequests { get; } = [];

        public ListEventsRequest? LastListEventsRequest { get; private set; }

        public ListSessionsRequest? LastListSessionsRequest { get; private set; }

        public Exception? ListEventsFailure { get; init; }

        public Exception? ListSessionsFailure { get; init; }

        public Task<CreateEventResponse> CreateEventAsync(
            CreateEventRequest request,
            CancellationToken cancellationToken)
        {
            CreateRequests.Add(request);
            var evt = new Event
            {
                EventId = Guid.NewGuid().ToString(),
                ActorId = request.ActorId,
                SessionId = request.SessionId,
                EventTimestamp = request.EventTimestamp,
                Payload = request.Payload
            };
            Events.Add(evt);
            return Task.FromResult(new CreateEventResponse { Event = evt });
        }

        public Task<ListEventsResponse> ListEventsAsync(
            ListEventsRequest request,
            CancellationToken cancellationToken)
        {
            LastListEventsRequest = request;
            if (ListEventsFailure is not null)
            {
                return Task.FromException<ListEventsResponse>(ListEventsFailure);
            }

            var sessionEvents = Events
                .Where(evt => evt.SessionId == request.SessionId)
                .OrderBy(evt => evt.EventTimestamp)
                .ToList();
            var offset = request.NextToken is null ? 0 : int.Parse(request.NextToken);
            var page = sessionEvents.Skip(offset).Take(request.MaxResults ?? 100).ToList();
            var nextOffset = offset + page.Count;
            var nextToken = nextOffset < sessionEvents.Count ? nextOffset.ToString() : null;
            return Task.FromResult(new ListEventsResponse { Events = page, NextToken = nextToken });
        }

        public Task<DeleteEventResponse> DeleteEventAsync(
            DeleteEventRequest request,
            CancellationToken cancellationToken)
        {
            DeleteRequests.Add(request);
            Events.RemoveAll(evt => evt.EventId == request.EventId);
            return Task.FromResult(new DeleteEventResponse());
        }

        public Task<ListSessionsResponse> ListSessionsAsync(
            ListSessionsRequest request,
            CancellationToken cancellationToken)
        {
            LastListSessionsRequest = request;
            if (ListSessionsFailure is not null)
            {
                return Task.FromException<ListSessionsResponse>(ListSessionsFailure);
            }

            var sessions = Events
                .Select(evt => evt.SessionId)
                .Distinct(StringComparer.Ordinal)
                .Select(sessionId => new SessionSummary { SessionId = sessionId })
                .ToList();
            var offset = request.NextToken is null ? 0 : int.Parse(request.NextToken);
            var page = sessions.Skip(offset).Take(request.MaxResults ?? 100).ToList();
            var nextOffset = offset + page.Count;
            var nextToken = nextOffset < sessions.Count ? nextOffset.ToString() : null;
            return Task.FromResult(new ListSessionsResponse { SessionSummaries = page, NextToken = nextToken });
        }

        public void AgeEvents(string sessionId, TimeSpan age)
        {
            foreach (var evt in Events.Where(evt => evt.SessionId == sessionId))
            {
                evt.EventTimestamp = DateTime.UtcNow - age;
            }
        }

        public void AgeMessageEvents(string sessionId, TimeSpan age)
        {
            foreach (var evt in Events.Where(evt =>
                         evt.SessionId == sessionId &&
                         evt.Payload?.Any(payload => payload.Conversational is not null) == true))
            {
                evt.EventTimestamp = DateTime.UtcNow - age;
            }
        }

        public void SeedEvents(string conversationId, int count)
        {
            var sessionId = AgentCoreMemoryConversationStore.GetSessionId(conversationId);
            for (var index = 0; index < count; index++)
            {
                Events.Add(new Event
                {
                    EventId = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    ActorId = "anonymous",
                    EventTimestamp = DateTime.UtcNow.AddSeconds(index),
                    Payload =
                    [
                        new PayloadType
                        {
                            Conversational = new Conversational
                            {
                                Role = "user",
                                Content = new Content { Text = $"message-{index}" }
                            }
                        },
                        new PayloadType
                        {
                            Json = new MemoryJsonData
                            {
                                Content = Document.FromObject(new { ConversationId = conversationId })
                            }
                        }
                    ]
                });
            }
        }
    }
}
