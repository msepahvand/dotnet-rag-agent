using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.Api.Services;

public sealed class AgentOrchestrationService(
    IAgentAnswerService agentAnswerService,
    IConversationStore conversationStore) : IAgentOrchestrationService
{
    public async Task<AgentAskResponse> AskAsync(AgentAskRequest request)
    {
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString()
            : request.ConversationId;

        var topK = TopKNormaliser.Normalise(request.TopK);

        var history = await conversationStore.GetHistoryAsync(conversationId);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("user", request.Question));

        var result = await agentAnswerService.AnswerAsync(request.Question, topK, history);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("assistant", result.Answer));

        return new AgentAskResponse
        {
            ConversationId = conversationId,
            ToolsUsed = result.ToolsUsed.Count > 0 ? result.ToolsUsed : ["search_posts"],
            Grounded = result.Grounded,
            Answer = result.Answer,
            Citations = result.Citations,
            Sources = result.Sources
        };
    }
}
