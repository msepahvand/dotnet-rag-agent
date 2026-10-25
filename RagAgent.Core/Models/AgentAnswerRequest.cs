namespace RagAgent.Core.Models;

public sealed record AgentAnswerRequest(
    string Question,
    int TopK,
    IReadOnlyList<ConversationMessage> History,
    string SessionId);
