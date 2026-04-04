using System.Diagnostics;
using RagAgent.Agents.Telemetry;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

public sealed class AgentOrchestrationService(
    IAgentAnswerService agentAnswerService,
    IConversationStore conversationStore) : IAgentOrchestrationService
{
    public async Task<AgentAskResponse> AskAsync(AgentAskRequest request)
    {
        using var activity = AgentActivitySource.Source.StartActivity("agent.ask");

        try
        {
            // Input guardrails: validate the user question before invoking the agent pipeline.
            AgentPipelineGuardrails.ValidateQuestion(request.Question);

            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString()
                : request.ConversationId;

            var topK = TopKNormaliser.Normalise(request.TopK);

            activity?.SetTag("conversation.id", conversationId);
            activity?.SetTag("rag.top_k", topK);

            var history = await conversationStore.GetHistoryAsync(conversationId);

            await conversationStore.AppendAsync(conversationId, new ChatMessage("user", request.Question));

            var result = await agentAnswerService.AnswerAsync(request.Question, topK, history);

            // Output guardrails: sanitise the answer before returning to the caller.
            result = SanitiseAnswer(result);

            activity?.SetTag("rag.grounded", result.Grounded);
            activity?.SetTag("rag.iterations", result.Iterations);
            activity?.SetTag("rag.citations_count", result.Citations.Count);
            activity?.SetTag("rag.tools_used", string.Join(",", result.ToolsUsed));

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
        catch (GuardrailException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Reason);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
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

        var answer = result.Answer.Length > AgentPipelineConstants.MaxAnswerLength
            ? result.Answer[..AgentPipelineConstants.MaxAnswerLength] + " … [response truncated]"
            : result.Answer;

        return result with
        {
            Answer = answer,
            Citations = verifiedCitations,
        };
    }
}
