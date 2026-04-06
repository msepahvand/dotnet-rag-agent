using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IResearcherAgent
{
    Task<ResearchResult> ResearchAsync(string question, int topK);
}
