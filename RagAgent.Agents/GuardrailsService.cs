using RagAgent.Agents.Filters;
using RagAgent.Core;

namespace RagAgent.Agents;

public sealed class GuardrailsService : IGuardrailsService
{
    public void ValidateQuestion(string question)
    {
        InputGuardrailFilter.CheckForInjection(question);
        InputGuardrailFilter.CheckForPii(question);
        InputGuardrailFilter.CheckTopicScope(question);
    }
}
