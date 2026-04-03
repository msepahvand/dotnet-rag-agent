using VectorSearch.Core.Models;

namespace VectorSearch.S3.Agents;

public interface IWriterAgent
{
    Task<AgentAnswerResult> WriteAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        string? criticFeedback = null);
}
