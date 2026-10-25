using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public interface IAgentOrchestrationService
{
    Task<AgentAskResponse> AskAsync(AgentAskRequest request);
}
