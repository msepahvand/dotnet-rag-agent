using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public interface IAgentOrchestrationService
{
    Task<AgentAskResponse> AskAsync(AgentAskRequest request);
}
