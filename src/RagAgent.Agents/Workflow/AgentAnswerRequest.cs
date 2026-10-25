using RagAgent.Core.Models;

namespace RagAgent.Agents.Workflow;

internal sealed record AgentWorkflowState(
    AgentAnswerRequest Request,
    ResearchResult Research,
    int Iteration = 0,
    AgentAnswerResult? Answer = null,
    bool Approved = false,
    string? CriticFeedback = null);
