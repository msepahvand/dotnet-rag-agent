using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IConversationStore
{
    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(string conversationId);
    Task AppendAsync(string conversationId, ConversationMessage message);
    Task DeleteAsync(string conversationId);
    Task<IReadOnlyList<string>> ListConversationIdsAsync();
}
