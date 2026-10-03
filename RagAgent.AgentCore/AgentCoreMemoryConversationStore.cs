using System.Security.Cryptography;
using System.Text;
using Amazon.BedrockAgentCore.Model;
using Amazon.Runtime.Documents;
using Microsoft.Extensions.Logging;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.AgentCore;

public sealed class AgentCoreMemoryConversationStore(
    IAgentCoreMemoryClient memoryClient,
    IDistributedConversationLock conversationLock,
    AgentCoreMemoryOptions options,
    ILogger<AgentCoreMemoryConversationStore> logger) : IConversationStore
{
    private const string ActorId = "anonymous";
    private const int PageSize = 100;
    private const int MaxMessagesPerConversation = 40;
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);
    public async Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(string conversationId)
    {
        var sessionId = GetSessionId(conversationId);
        await using var sessionLease = await conversationLock.AcquireAsync(sessionId);
        var cancellationToken = sessionLease.CancellationToken;
        var events = await GetEventsAsync(sessionId, includePayloads: true, cancellationToken);
        if (IsExpired(events))
        {
            await DeleteEventsAsync(sessionId, events, cancellationToken);
            logger.LogDebug("Expired conversation was removed from AgentCore Memory.");
            sessionLease.EnsureValid();
            return [];
        }

        var history = events
            .Where(evt => GetConversationId(evt) == conversationId)
            .OrderBy(evt => evt.EventTimestamp)
            .Select(GetMessage)
            .Where(message => message is not null)
            .Cast<ConversationMessage>()
            .TakeLast(MaxMessagesPerConversation)
            .ToList();

        if (history.Count > 0)
        {
            await CreateActivityEventAsync(conversationId, sessionId, cancellationToken);
            await DeleteEventsAsync(sessionId, events.Where(IsActivityEvent).ToList(), cancellationToken);
        }

        sessionLease.EnsureValid();
        return history;
    }

    public async Task AppendAsync(string conversationId, ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sessionId = GetSessionId(conversationId);
        await using var sessionLease = await conversationLock.AcquireAsync(sessionId);
        var cancellationToken = sessionLease.CancellationToken;
        var events = await GetEventsAsync(sessionId, includePayloads: true, cancellationToken);
        if (IsExpired(events))
        {
            await DeleteEventsAsync(sessionId, events, cancellationToken);
            events.Clear();
        }

        var createdEvent = await ExecuteMemoryRequestAsync(
            () => memoryClient.CreateEventAsync(
                new CreateEventRequest
                {
                    MemoryId = options.MemoryId,
                    ActorId = ActorId,
                    SessionId = sessionId,
                    EventTimestamp = DateTime.UtcNow,
                    Payload =
                    [
                        new PayloadType
                        {
                            Conversational = new Conversational
                            {
                                Role = message.Role,
                                Content = new Content { Text = message.Content }
                            }
                        },
                        CreateIdentityPayload(conversationId)
                    ]
                },
                cancellationToken));
        if (createdEvent.Event is null)
        {
            throw new InvalidOperationException("AgentCore Memory did not return the created conversation event.");
        }

        events.Add(createdEvent.Event);
        await DeleteEventsAsync(sessionId, events.Where(IsActivityEvent).ToList(), cancellationToken);
        var conversationEvents = events.Where(evt => GetMessage(evt) is not null).ToList();
        var excessEvents = conversationEvents
            .OrderBy(evt => evt.EventTimestamp)
            .Take(Math.Max(0, conversationEvents.Count - MaxMessagesPerConversation))
            .ToList();
        await DeleteEventsAsync(sessionId, excessEvents, cancellationToken);
        sessionLease.EnsureValid();
    }

    public async Task DeleteAsync(string conversationId)
    {
        var sessionId = GetSessionId(conversationId);
        await using var sessionLease = await conversationLock.AcquireAsync(sessionId);
        var cancellationToken = sessionLease.CancellationToken;
        var events = await GetEventsAsync(sessionId, includePayloads: false, cancellationToken);
        await DeleteEventsAsync(sessionId, events, cancellationToken);
        sessionLease.EnsureValid();
    }

    public async Task<IReadOnlyList<string>> ListConversationIdsAsync()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? nextToken = null;
        do
        {
            var response = await ExecuteMemoryRequestAsync(
                () => memoryClient.ListSessionsAsync(
                    new ListSessionsRequest
                    {
                        MemoryId = options.MemoryId,
                        ActorId = ActorId,
                        MaxResults = PageSize,
                        NextToken = nextToken
                    },
                    CancellationToken.None));

            foreach (var session in response.SessionSummaries ?? [])
            {
                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    continue;
                }

                await using var sessionLease = await conversationLock.AcquireAsync(session.SessionId);
                var cancellationToken = sessionLease.CancellationToken;
                var events = await GetEventsAsync(session.SessionId, includePayloads: true, cancellationToken);
                if (IsExpired(events))
                {
                    await DeleteEventsAsync(session.SessionId, events, cancellationToken);
                    logger.LogDebug("Expired conversation was removed from AgentCore Memory.");
                    sessionLease.EnsureValid();
                    continue;
                }

                foreach (var evt in events)
                {
                    var id = GetConversationId(evt);
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }

                sessionLease.EnsureValid();
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return ids.Order(StringComparer.Ordinal).ToList();
    }

    public static string GetSessionId(string conversationId)
    {
        ArgumentNullException.ThrowIfNull(conversationId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(conversationId))).ToLowerInvariant();
    }

    private async Task CreateActivityEventAsync(
        string conversationId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteMemoryRequestAsync(
            () => memoryClient.CreateEventAsync(
                new CreateEventRequest
                {
                    MemoryId = options.MemoryId,
                    ActorId = ActorId,
                    SessionId = sessionId,
                    EventTimestamp = DateTime.UtcNow,
                    Payload = [CreateIdentityPayload(conversationId, activity: true)]
                },
                cancellationToken));

        if (response.Event is null)
        {
            throw new InvalidOperationException("AgentCore Memory did not return the activity event.");
        }
    }

    private async Task<List<Event>> GetEventsAsync(
        string sessionId,
        bool includePayloads,
        CancellationToken cancellationToken)
    {
        var events = new List<Event>();
        string? nextToken = null;
        do
        {
            ListEventsResponse response;
            try
            {
                response = await ExecuteMemoryRequestAsync(
                    () => memoryClient.ListEventsAsync(
                        new ListEventsRequest
                        {
                            MemoryId = options.MemoryId,
                            ActorId = ActorId,
                            SessionId = sessionId,
                            MaxResults = PageSize,
                            IncludePayloads = includePayloads,
                            NextToken = nextToken
                        },
                        cancellationToken));
            }
            catch (ConversationStoreNotFoundException) when (nextToken is null)
            {
                // A conversation without a session is a normal empty-history case.
                return [];
            }

            if (response.Events is not null)
            {
                events.AddRange(response.Events);
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

        return events;
    }

    private async Task DeleteEventsAsync(
        string sessionId,
        IReadOnlyList<Event> events,
        CancellationToken cancellationToken)
    {
        foreach (var evt in events)
        {
            if (string.IsNullOrWhiteSpace(evt.EventId))
            {
                continue;
            }

            await ExecuteMemoryRequestAsync(
                () => memoryClient.DeleteEventAsync(
                    new DeleteEventRequest
                    {
                        MemoryId = options.MemoryId,
                        ActorId = ActorId,
                        SessionId = sessionId,
                        EventId = evt.EventId
                    },
                    cancellationToken));
        }
    }

    private static async Task<T> ExecuteMemoryRequestAsync<T>(Func<Task<T>> request)
    {
        try
        {
            return await request();
        }
        catch (ValidationException exception)
        {
            throw new ConversationStoreValidationException("The conversation request is invalid.", exception);
        }
        catch (ResourceNotFoundException exception)
        {
            throw new ConversationStoreNotFoundException("The conversation store was not found.", exception);
        }
    }

    private static bool IsExpired(IReadOnlyList<Event> events)
    {
        var latestEvent = events
            .Where(evt => evt.EventTimestamp.HasValue)
            .Select(evt => evt.EventTimestamp!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        return latestEvent == DateTime.MinValue || DateTime.UtcNow - latestEvent > ConversationTtl;
    }

    private static string? GetConversationId(Event evt)
    {
        var identity = evt.Payload?.FirstOrDefault(payload => payload.Json is not null)?.Json?.Content;
        if (!identity.HasValue ||
            !identity.Value.IsDictionary() ||
            !identity.Value.AsDictionary().TryGetValue("ConversationId", out var id) ||
            !id.IsString())
        {
            return null;
        }

        return id.AsString();
    }

    private static bool IsActivityEvent(Event evt)
    {
        var identity = evt.Payload?.FirstOrDefault(payload => payload.Json is not null)?.Json?.Content;
        return identity.HasValue &&
               identity.Value.IsDictionary() &&
               identity.Value.AsDictionary().TryGetValue("Activity", out var activity) &&
               activity.IsBool() &&
               activity.AsBool();
    }

    private static ConversationMessage? GetMessage(Event evt)
    {
        var conversational = evt.Payload?.FirstOrDefault(payload => payload.Conversational is not null)?.Conversational;
        return conversational is null
            ? null
            : new ConversationMessage(conversational.Role, conversational.Content.Text);
    }

    private static PayloadType CreateIdentityPayload(string conversationId, bool activity = false) =>
        new()
        {
            Json = new MemoryJsonData
            {
                Content = Document.FromObject(new ConversationIdentity(conversationId, activity))
            }
        };

    private sealed record ConversationIdentity(string ConversationId, bool Activity);
}
