using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

public sealed class GuardrailsService : IGuardrailsService
{
    public Task ValidateQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegexGuardrails.CheckForInjection(question);
        RegexGuardrails.CheckForPii(question);
        RegexGuardrails.CheckTopicScope(question);
        return Task.CompletedTask;
    }

    public Task<GuardrailEvaluation> ValidateAnswerAsync(
        string answer,
        IReadOnlyList<AgentSource> sources,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GuardrailEvaluation());
    }
}
