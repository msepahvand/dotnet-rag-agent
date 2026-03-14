namespace VectorSearch.Core;

public interface IAgentAnswerService
{
    Task<string> BuildGroundedAnswerAsync(string question, List<AgentSource> sources);
}
