using RagAgent.Core.Models;

namespace RagAgent.Agents.Agents;

public interface ICriticAgent
{
    Task<CriticResult> EvaluateAsync(string question, AgentAnswerResult answer, ResearchResult research);
}
