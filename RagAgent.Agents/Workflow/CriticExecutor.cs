using Microsoft.Agents.AI.Workflows;
using RagAgent.Core;

namespace RagAgent.Agents.Workflow;

internal sealed class CriticExecutor(ICriticAgent criticAgent)
    : Executor<AgentWorkflowState, AgentWorkflowState>("Critic")
{
    public override async ValueTask<AgentWorkflowState> HandleAsync(
        AgentWorkflowState state,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = state.Answer
            ?? throw new InvalidOperationException("The critic received workflow state without a draft.");
        var result = await criticAgent.EvaluateAsync(
            state.Request.Question,
            answer,
            state.Research);

        return state with
        {
            Approved = result.Approved,
            CriticFeedback = result.Feedback,
        };
    }
}
