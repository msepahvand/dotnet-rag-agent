using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using RagAgent.Agents.Agents;

namespace RagAgent.Agents.Process.Steps;

/// <summary>
/// Evaluates a writer draft. Emits <see cref="Events.Approved"/> when the answer meets quality
/// criteria, or <see cref="Events.RevisionRequested"/> with structured feedback for the writer
/// to address on the next iteration.
/// </summary>
public sealed class CriticStep : KernelProcessStep
{
    public static class Events
    {
        public const string Approved = nameof(Approved);
        public const string RevisionRequested = nameof(RevisionRequested);
    }

    [KernelFunction]
    public async Task EvaluateAsync(
        KernelProcessStepContext context,
        Kernel kernel,
        DraftPayload payload)
    {
        var critic = kernel.Services.GetRequiredService<ICriticAgent>();
        var result = await critic.EvaluateAsync(payload.Question, payload.Answer, payload.Research);

        if (result.Approved)
        {
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = Events.Approved,
                Data = payload.Answer,
            });
        }
        else
        {
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = Events.RevisionRequested,
                Data = new RevisionPayload(
                    payload.Question,
                    payload.History,
                    payload.Research,
                    payload.Answer,
                    result.Feedback),
            });
        }
    }
}
