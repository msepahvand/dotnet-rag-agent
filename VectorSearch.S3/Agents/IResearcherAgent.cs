using VectorSearch.Core.Models;

namespace VectorSearch.S3.Agents;

public interface IResearcherAgent
{
    Task<ResearchResult> ResearchAsync(string question, int topK);
}
