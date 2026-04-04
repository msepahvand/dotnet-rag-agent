using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using RagAgent.Agents.Agents;

namespace RagAgent.Agents.Process.Steps;

/// <summary>
/// First step in the RAG process. Retrieves relevant sources via <see cref="IResearcherAgent"/>
/// and emits <see cref="Events.ResearchComplete"/> for the writer step to consume.
/// </summary>
public sealed class ResearchStep : KernelProcessStep
{
    public static class Events
    {
        public const string ResearchComplete = nameof(ResearchComplete);
    }

    [KernelFunction]
    public async Task ResearchAsync(
        KernelProcessStepContext context,
        Kernel kernel,
        AgentAnswerRequest request)
    {
        var researcher = kernel.Services.GetRequiredService<IResearcherAgent>();
        var research = await researcher.ResearchAsync(request.Question, request.TopK);

        await context.EmitEventAsync(new KernelProcessEvent
        {
            Id = Events.ResearchComplete,
            Data = new ResearchPayload(request.Question, request.History, research),
        });
    }
}
