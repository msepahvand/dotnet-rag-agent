using Microsoft.Agents.AI.Workflows;
using RagAgent.Core;
using RagAgent.Core.Models;
using System.Runtime.CompilerServices;

namespace RagAgent.Agents.Workflow;

public sealed class AgentAnswerWorkflowService(
    IResearcherAgent researcherAgent,
    IWriterAgent writerAgent,
    ICriticAgent criticAgent) : IAgentAnswerService
{
    private const int MaxIterations = 3;

    public async Task<AgentAnswerResult> AnswerAsync(AgentAnswerRequest request)
    {
        request = request with { TopK = TopKNormaliser.Normalise(request.TopK) };
        var workflow = BuildWorkflow();

        await using var run = await InProcessExecution.RunAsync(workflow, request);
        return run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.Data)
            .OfType<AgentAnswerResult>()
            .SingleOrDefault()
            ?? throw new InvalidOperationException("The agent workflow completed without producing an answer.");
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var topK = TopKNormaliser.Normalise(request.TopK);

        yield return AgentStreamEvent.ForStatus("Searching for relevant sources…");

        var research = await researcherAgent.ResearchAsync(request.Question, topK);
        yield return AgentStreamEvent.ForSources(research.Sources.ToList());
        yield return AgentStreamEvent.ForStatus("Generating answer…");

        await foreach (var token in writerAgent.StreamAsync(
            request.Question,
            research,
            request.History,
            cancellationToken))
        {
            yield return AgentStreamEvent.ForToken(token);
        }

        yield return AgentStreamEvent.ForDone(research.Sources.Count > 0, research.Sources.ToList());
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
