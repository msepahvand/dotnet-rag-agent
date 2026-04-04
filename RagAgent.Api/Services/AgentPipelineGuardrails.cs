using RagAgent.Agents.Filters;
using RagAgent.Core;

namespace RagAgent.Api.Services;

/// <summary>
/// Shared input guardrail validation used by both the batch and streaming agent pipelines.
/// </summary>
internal static class AgentPipelineGuardrails
{
    /// <summary>
    /// Validates the question against prompt injection, PII, and topic scope rules.
    /// Throws <see cref="GuardrailException"/> on violation.
    /// </summary>
    internal static void ValidateQuestion(string question)
    {
        InputGuardrailFilter.CheckForInjection(question);
        InputGuardrailFilter.CheckForPii(question);
        InputGuardrailFilter.CheckTopicScope(question);
    }
}
