using RagAgent.Agents.Filters;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public sealed class AgentOrchestrationService(
    IAgentAnswerService agentAnswerService,
    IConversationStore conversationStore) : IAgentOrchestrationService
{
    private const int MaxAnswerLength = 3_000;

    public async Task<AgentAskResponse> AskAsync(AgentAskRequest request)
    {
        // Input guardrails: validate the user question before invoking the agent pipeline.
        ValidateQuestion(request.Question);

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString()
            : request.ConversationId;

        var topK = TopKNormaliser.Normalise(request.TopK);

        var history = await conversationStore.GetHistoryAsync(conversationId);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("user", request.Question));

        var result = await agentAnswerService.AnswerAsync(request.Question, topK, history);

        // Output guardrails: sanitise the answer before returning to the caller.
        result = SanitiseAnswer(result);

        await conversationStore.AppendAsync(conversationId, new ChatMessage("assistant", result.Answer));

        return new AgentAskResponse
        {
            ConversationId = conversationId,
            ToolsUsed = result.ToolsUsed.Count > 0 ? result.ToolsUsed : ["search_posts"],
            Grounded = result.Grounded,
            Answer = result.Answer,
            Citations = result.Citations,
            Sources = result.Sources,
            Iterations = result.Iterations
        };
    }

    // ── Input guardrails ─────────────────────────────────────────────────────

    /// <summary>
    /// Validates the user question against prompt injection, PII, and topic scope rules.
    /// Delegates to the same checks used by <see cref="InputGuardrailFilter"/> so both
    /// code paths are consistent.
    /// </summary>
    private static void ValidateQuestion(string question)
    {
        InputGuardrailFilter.CheckForInjection(question);
        InputGuardrailFilter.CheckForPii(question);
        InputGuardrailFilter.CheckTopicScope(question);
    }

    // ── Output guardrails ────────────────────────────────────────────────────

    /// <summary>
    /// Sanitises the agent answer by:
    /// <list type="bullet">
    ///   <item>Stripping citations whose PostId is not present in the retrieved sources.</item>
    ///   <item>Truncating the answer text if it exceeds <see cref="MaxAnswerLength"/> characters.</item>
    /// </list>
    /// </summary>
    private static AgentAnswerResult SanitiseAnswer(AgentAnswerResult result)
    {
        var validPostIds = result.Sources.Select(s => s.PostId).ToHashSet();

        var verifiedCitations = result.Citations
            .Where(c => validPostIds.Contains(c.PostId))
            .ToList();

        var answer = result.Answer.Length > MaxAnswerLength
            ? result.Answer[..MaxAnswerLength] + " … [response truncated]"
            : result.Answer;

        return result with
        {
            Answer = answer,
            Citations = verifiedCitations,
        };
    }
}
