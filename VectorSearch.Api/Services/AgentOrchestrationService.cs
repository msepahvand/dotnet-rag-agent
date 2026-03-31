using VectorSearch.Core;

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

        var topK = request.TopK <= 0 ? 5 : Math.Min(request.TopK, 10);

        var history = await conversationStore.GetHistoryAsync(conversationId);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("user", request.Question));

        var result = await agentAnswerService.AnswerAsync(request.Question, topK, history);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("assistant", result.Answer));

        return new AgentAskResponse
        {
            ConversationId = conversationId,
            ToolUsed = "semantic-search",
            Grounded = result.Grounded,
            Answer = result.Answer,
            Citations = result.Citations,
            Sources = result.Sources
        };
    }
}
