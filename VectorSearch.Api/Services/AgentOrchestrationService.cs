using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class AgentOrchestrationService(
    IAgentAnswerService agentAnswerService) : IAgentOrchestrationService
{
    public async Task<AgentAskResponse> AskAsync(AgentAskRequest request)
    {
        var topK = request.TopK <= 0 ? 5 : Math.Min(request.TopK, 10);
        var result = await agentAnswerService.AnswerAsync(request.Question, topK);

        return new AgentAskResponse
        {
            ToolUsed = "semantic-search",
            Grounded = result.Grounded,
            Answer = result.Answer,
            Citations = result.Citations,
            Sources = result.Sources
        };
    }
}
