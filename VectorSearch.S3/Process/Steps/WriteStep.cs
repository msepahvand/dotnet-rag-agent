using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using VectorSearch.Core.Models;
using VectorSearch.S3.Agents;

namespace VectorSearch.S3.Process.Steps;

/// <summary>
/// Stateful synthesis step. Tracks the number of write attempts and skips critic evaluation
/// on the final iteration to guarantee the process terminates.
/// </summary>
public sealed class WriteStep : KernelProcessStep<WriteStep.State>
{
    private const int MaxIterations = 3;

    public sealed class State
    {
        public int Iteration { get; set; }
    }

    public static class Events
    {
        /// <summary>Emitted when there are iterations remaining; routes to <see cref="CriticStep"/>.</summary>
        public const string DraftReady = nameof(DraftReady);

        /// <summary>Emitted on the final iteration; routes directly to <see cref="OutputStep"/>.</summary>
        public const string FinalAnswer = nameof(FinalAnswer);
    }

    private State _state = new();

    public override ValueTask ActivateAsync(KernelProcessStepState<State> state)
    {
        _state = state.State ?? new State();
        return ValueTask.CompletedTask;
    }

    /// <summary>Called on the first write attempt (no critic feedback yet).</summary>
    [KernelFunction]
    public async Task Write(
        KernelProcessStepContext context,
        Kernel kernel,
        ResearchPayload payload)
    {
        var writer = kernel.Services.GetRequiredService<IWriterAgent>();
        var answer = await writer.WriteAsync(payload.Question, payload.Research, payload.History);
        await EmitAnswerEventAsync(context, payload.Question, payload.History, payload.Research, answer);
    }

    /// <summary>Called on subsequent attempts with critic feedback injected.</summary>
    [KernelFunction]
    public async Task Revise(
        KernelProcessStepContext context,
        Kernel kernel,
        RevisionPayload payload)
    {
        var writer = kernel.Services.GetRequiredService<IWriterAgent>();
        var answer = await writer.WriteAsync(
            payload.Question, payload.Research, payload.History, payload.CriticFeedback);
        await EmitAnswerEventAsync(context, payload.Question, payload.History, payload.Research, answer);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task EmitAnswerEventAsync(
        KernelProcessStepContext context,
        string question,
        IReadOnlyList<ChatMessage> history,
        ResearchResult research,
        AgentAnswerResult answer)
    {
        _state.Iteration++;
        var isFinal = _state.Iteration >= MaxIterations;
        var answerWithIterations = answer with { Iterations = _state.Iteration };

        if (isFinal)
        {
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = Events.FinalAnswer,
                Data = answerWithIterations,
            });
        }
        else
        {
            await context.EmitEventAsync(new KernelProcessEvent
            {
                Id = Events.DraftReady,
                Data = new DraftPayload(question, history, research, answerWithIterations),
            });
        }
    }
}
