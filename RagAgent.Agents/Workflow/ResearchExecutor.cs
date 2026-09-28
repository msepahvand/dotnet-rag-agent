using Microsoft.Agents.AI.Workflows;
using RagAgent.Core;

namespace RagAgent.Agents.Workflow;

internal sealed class ResearchExecutor(IResearcherAgent researcherAgent)
    : Executor<AgentAnswerRequest, AgentWorkflowState>("Research")
{
    public override async ValueTask<AgentWorkflowState> HandleAsync(
        AgentAnswerRequest request,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var research = await researcherAgent.ResearchAsync(request.Question, request.TopK);
        return new AgentWorkflowState(request, research);
    }
}
