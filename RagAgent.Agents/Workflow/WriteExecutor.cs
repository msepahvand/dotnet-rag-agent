using Microsoft.Agents.AI.Workflows;
using RagAgent.Core;

namespace RagAgent.Agents.Workflow;

internal sealed class WriteExecutor(IWriterAgent writerAgent)
    : Executor<AgentWorkflowState, AgentWorkflowState>("Write")
{
    public override async ValueTask<AgentWorkflowState> HandleAsync(
        AgentWorkflowState state,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var iteration = state.Iteration + 1;
        var answer = await writerAgent.WriteAsync(
            state.Request.Question,
            state.Research,
            state.Request.History,
            state.CriticFeedback);

        return state with
        {
            Iteration = iteration,
            Answer = answer with { Iterations = iteration },
            Approved = false,
        };
    }
}
