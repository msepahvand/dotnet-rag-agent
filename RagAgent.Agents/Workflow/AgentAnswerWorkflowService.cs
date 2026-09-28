using Microsoft.Agents.AI.Workflows;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents.Workflow;

public sealed class AgentAnswerWorkflowService(
    IResearcherAgent researcherAgent,
    IWriterAgent writerAgent,
    ICriticAgent criticAgent) : IAgentAnswerService
{
    private const int MaxIterations = 3;

    public async Task<AgentAnswerResult> AnswerAsync(
        string question,
        int topK,
        IReadOnlyList<ConversationMessage> history)
    {
        var normalisedTopK = TopKNormaliser.Normalise(topK);
        var request = new AgentAnswerRequest(question, normalisedTopK, history);
        var workflow = BuildWorkflow();

        await using var run = await InProcessExecution.RunAsync(workflow, request);
        return run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.Data)
            .OfType<AgentAnswerResult>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException("The agent workflow completed without producing an answer.");
    }

    private Microsoft.Agents.AI.Workflows.Workflow BuildWorkflow()
    {
        var research = new ResearchExecutor(researcherAgent);
        var write = new WriteExecutor(writerAgent);
        var critic = new CriticExecutor(criticAgent);
        var output = new OutputExecutor();

        return new WorkflowBuilder(research)
            .AddEdge(research, write)
            .AddEdge<AgentWorkflowState>(
                write,
                critic,
                state => state is not null && state.Iteration < MaxIterations)
            .AddEdge<AgentWorkflowState>(
                write,
                output,
                state => state is not null && state.Iteration >= MaxIterations)
            .AddEdge<AgentWorkflowState>(
                critic,
                output,
                state => state is not null && state.Approved)
            .AddEdge<AgentWorkflowState>(
                critic,
                write,
                state => state is not null && !state.Approved)
            .WithOutputFrom(output)
            .WithOpenTelemetry(configure: telemetry => telemetry.EnableSensitiveData = false)
            .Build();
    }
}
