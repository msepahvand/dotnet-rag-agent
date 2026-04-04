using Microsoft.SemanticKernel;
using RagAgent.Core;
using RagAgent.Core.Models;
using RagAgent.Agents.Process.Steps;

namespace RagAgent.Agents.Process;

/// <summary>
/// <see cref="IAgentAnswerService"/> implementation backed by an SK <see cref="KernelProcess"/>.
///
/// Process topology:
/// <code>
///   Start → ResearchStep → WriteStep.Write ──┐
///                                            ↓ DraftReady
///                                        CriticStep
///                                       ↙         ↘
///                              Approved          RevisionRequested
///                                 ↓                     ↓
///                            OutputStep         WriteStep.Revise
///                                                     ↓ DraftReady (loop)
///                                          (FinalAnswer goes directly to OutputStep)
/// </code>
/// </summary>
public sealed class ProcessAnswerService : IAgentAnswerService
{
    private static readonly KernelProcess Process = BuildProcess();

    private readonly Kernel _kernel;
    private readonly ProcessResultHolder _resultHolder;

    public ProcessAnswerService(Kernel kernel, ProcessResultHolder resultHolder)
    {
        _kernel = kernel;
        _resultHolder = resultHolder;
    }

    public async Task<AgentAnswerResult> AnswerAsync(
        string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var normalisedTopK = TopKNormaliser.Normalise(topK);
        var request = new AgentAnswerRequest(question, normalisedTopK, history);

        await Process.StartAsync(_kernel, new KernelProcessEvent
        {
            Id = ProcessEvents.Start,
            Data = request,
        });

        return _resultHolder.Result
            ?? throw new InvalidOperationException("SK Process completed without producing a result.");
    }

    // ── Process definition ────────────────────────────────────────────────────
    private static class ProcessEvents
    {
        public const string Start = nameof(Start);
    }

    private static KernelProcess BuildProcess()
    {
        var builder = new ProcessBuilder("RagAgentProcess");

        var researchStep = builder.AddStepFromType<ResearchStep>();
        var writeStep = builder.AddStepFromType<WriteStep>();
        var criticStep = builder.AddStepFromType<CriticStep>();
        var outputStep = builder.AddStepFromType<OutputStep>();

        // Start → Research
        builder
            .OnInputEvent(ProcessEvents.Start)
            .SendEventTo(new ProcessFunctionTargetBuilder(researchStep));

        // Research complete → first write
        researchStep
            .OnEvent(ResearchStep.Events.ResearchComplete)
            .SendEventTo(new ProcessFunctionTargetBuilder(writeStep, "Write"));

        // Draft ready → critic evaluation
        writeStep
            .OnEvent(WriteStep.Events.DraftReady)
            .SendEventTo(new ProcessFunctionTargetBuilder(criticStep));

        // Critic approves → capture output
        criticStep
            .OnEvent(CriticStep.Events.Approved)
            .SendEventTo(new ProcessFunctionTargetBuilder(outputStep));

        // Critic requests revision → writer revises (loop)
        criticStep
            .OnEvent(CriticStep.Events.RevisionRequested)
            .SendEventTo(new ProcessFunctionTargetBuilder(writeStep, "Revise"));

        // Final iteration → skip critic, capture output directly
        writeStep
            .OnEvent(WriteStep.Events.FinalAnswer)
            .SendEventTo(new ProcessFunctionTargetBuilder(outputStep));

        return builder.Build();
    }
}
