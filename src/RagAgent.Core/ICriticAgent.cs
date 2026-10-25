using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface ICriticAgent
{
    Task<CriticResult> EvaluateAsync(string question, AgentAnswerResult answer, ResearchResult research);
}
