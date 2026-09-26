using RagAgent.Core;

namespace RagAgent.Agents;

public sealed class GuardrailsService : IGuardrailsService
{
    public void ValidateQuestion(string question)
    {
        RegexGuardrails.CheckForInjection(question);
        RegexGuardrails.CheckForPii(question);
        RegexGuardrails.CheckTopicScope(question);
    }
}
