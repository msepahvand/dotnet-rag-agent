using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IConversationStore
{
    IAsyncEnumerable<ConversationEvent> Subscribe(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string conversationId);
    Task AppendAsync(string conversationId, ChatMessage message);
    Task DeleteAsync(string conversationId);
    Task<IReadOnlyList<string>> ListConversationIdsAsync();
}
