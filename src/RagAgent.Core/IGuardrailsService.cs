namespace RagAgent.Core;

public interface IGuardrailsService
{
    /// <summary>
    /// Validates the question against prompt injection, PII, and topic scope rules.
    /// Throws <see cref="GuardrailException"/> on violation.
    /// </summary>
    Task ValidateQuestionAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the final sanitised answer against the retrieved source material.
    /// </summary>
    Task<GuardrailEvaluation> ValidateAnswerAsync(
        string answer,
        IReadOnlyList<Models.AgentSource> sources,
        CancellationToken cancellationToken = default);
}
