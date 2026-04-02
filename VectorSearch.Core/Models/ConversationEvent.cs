namespace VectorSearch.Core.Models;

public abstract record ConversationEvent(string ConversationId)
{
    public sealed record MessageAppended(string ConversationId, ChatMessage Message)
        : ConversationEvent(ConversationId);

    public sealed record ConversationDeleted(string ConversationId)
        : ConversationEvent(ConversationId);

    public sealed record ConversationExpired(string ConversationId)
        : ConversationEvent(ConversationId);
}
