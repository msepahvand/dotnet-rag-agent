using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IConversationStore
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(string conversationId);
    Task AppendAsync(string conversationId, ChatMessage message);
    Task DeleteAsync(string conversationId);
    Task<IReadOnlyList<string>> ListConversationIdsAsync();
}
