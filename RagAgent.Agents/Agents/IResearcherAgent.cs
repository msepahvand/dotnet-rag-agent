using RagAgent.Core.Models;

namespace RagAgent.Agents.Agents;

public interface IResearcherAgent
{
    Task<ResearchResult> ResearchAsync(string question, int topK);
}
