using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IAgentAnswerService
{
    Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history);
}
