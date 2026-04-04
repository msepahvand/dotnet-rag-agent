using RagAgent.Core.Models;

namespace RagAgent.Agents.Agents;

public interface IWriterAgent
{
    Task<AgentAnswerResult> WriteAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        string? criticFeedback = null);
}
