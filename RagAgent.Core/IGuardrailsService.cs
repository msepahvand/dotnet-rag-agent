namespace RagAgent.Core;

public interface IGuardrailsService
{
    /// <summary>
    /// Validates the question against prompt injection, PII, and topic scope rules.
    /// Throws <see cref="GuardrailException"/> on violation.
    /// </summary>
    void ValidateQuestion(string question);
}
