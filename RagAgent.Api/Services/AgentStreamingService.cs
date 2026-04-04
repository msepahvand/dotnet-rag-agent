using System.Runtime.CompilerServices;
using RagAgent.Agents.Agents;
using RagAgent.Agents.Telemetry;
using RagAgent.Api.Dtos;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

/// <summary>
/// Streaming variant of the agent pipeline. Bypasses the SK Process framework to allow
/// token-level SSE streaming: research → stream write (plain-text prose, no critic loop).
/// </summary>
public sealed class AgentStreamingService(
    IResearcherAgent researcherAgent,
    IWriterAgent writerAgent,
    IConversationStore conversationStore) : IAgentStreamingService
{
    public async IAsyncEnumerable<StreamEventDto> StreamAsync(
        AgentAskRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = AgentActivitySource.Source.StartActivity("agent.stream");

        // Input guardrails — store any violation outside try/catch so we can yield after.
        var guardrailViolation = CaptureGuardrailViolation(request.Question);
        if (guardrailViolation is not null)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, guardrailViolation);
            yield return StreamEventDto.ForError(guardrailViolation);
            yield break;
        }

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString()
            : request.ConversationId;

        var topK = TopKNormaliser.Normalise(request.TopK);

        activity?.SetTag("conversation.id", conversationId);
        activity?.SetTag("rag.top_k", topK);

        var history = await conversationStore.GetHistoryAsync(conversationId);
        await conversationStore.AppendAsync(conversationId, new ChatMessage("user", request.Question));

        // ── Research ─────────────────────────────────────────────────────────
        yield return StreamEventDto.ForStatus("Searching for relevant sources…");

        var research = await researcherAgent.ResearchAsync(request.Question, topK);

        activity?.SetTag("rag.sources_count", research.Sources.Count);

        yield return StreamEventDto.ForSources(research.Sources.Select(SourceMapper.ToDto).ToList());

        // ── Stream the answer ─────────────────────────────────────────────────
        yield return StreamEventDto.ForStatus("Generating answer…");

        var buffer = new System.Text.StringBuilder();

        await foreach (var token in writerAgent.StreamAsync(request.Question, research, history, ct))
        {
            buffer.Append(token);
            yield return StreamEventDto.ForToken(token);
        }

        var answer = buffer.ToString().Trim();

        // Output guardrail: length limit.
        if (answer.Length > AgentPipelineConstants.MaxAnswerLength)
        {
            answer = answer[..AgentPipelineConstants.MaxAnswerLength] + " … [response truncated]";
        }

        await conversationStore.AppendAsync(conversationId, new ChatMessage("assistant", answer));

        activity?.SetTag("rag.grounded", research.Sources.Count > 0);

        yield return StreamEventDto.ForDone(
            conversationId,
            grounded: research.Sources.Count > 0,
            sources: research.Sources.Select(SourceMapper.ToDto).ToList());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs input guardrail checks and returns the violation reason, or null if clean.
    /// Cannot throw inside an async iterator, so we capture the result here instead.
    /// </summary>
    private static string? CaptureGuardrailViolation(string question)
    {
        try
        {
            AgentPipelineGuardrails.ValidateQuestion(question);
            return null;
        }
        catch (GuardrailException ex)
        {
            return ex.Reason;
        }
    }
}
