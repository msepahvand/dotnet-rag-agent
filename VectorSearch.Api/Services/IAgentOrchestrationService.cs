using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public interface IAgentOrchestrationService
{
    Task<AgentAskResponse> AskAsync(AgentAskRequest request);
}
