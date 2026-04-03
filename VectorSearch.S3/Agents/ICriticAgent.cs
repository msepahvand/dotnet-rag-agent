using VectorSearch.Core.Models;

namespace VectorSearch.S3.Agents;

public interface ICriticAgent
{
    Task<CriticResult> EvaluateAsync(string question, AgentAnswerResult answer, ResearchResult research);
}
