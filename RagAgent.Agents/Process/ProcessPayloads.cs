using RagAgent.Core.Models;

namespace RagAgent.Agents.Process;

/// <summary>Internal event payloads passed between SK Process steps.</summary>
public sealed record AgentAnswerRequest(string Question, int TopK, IReadOnlyList<ChatMessage> History);

public sealed record ResearchPayload(
    string Question,
    IReadOnlyList<ChatMessage> History,
    ResearchResult Research);

public sealed record DraftPayload(
    string Question,
    IReadOnlyList<ChatMessage> History,
    ResearchResult Research,
    AgentAnswerResult Answer);

public sealed record RevisionPayload(
    string Question,
    IReadOnlyList<ChatMessage> History,
    ResearchResult Research,
    AgentAnswerResult PreviousAnswer,
    string CriticFeedback);
