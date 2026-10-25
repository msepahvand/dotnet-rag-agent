using Microsoft.Agents.AI.Workflows;
using RagAgent.Core.Models;

namespace RagAgent.Agents.Workflow;

[YieldsOutput(typeof(AgentAnswerResult))]
internal sealed partial class OutputExecutor()
    : Executor<AgentWorkflowState>("Output")
{
    public override async ValueTask HandleAsync(
        AgentWorkflowState state,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var answer = state.Answer
            ?? throw new InvalidOperationException("The workflow reached output without a draft.");
        await context.YieldOutputAsync(answer, cancellationToken);
    }
}
