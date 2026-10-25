using System.Runtime.CompilerServices;
using RagAgent.Agents.Telemetry;
using RagAgent.Api.Dtos;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Services;

/// <summary>
/// Streaming variant of the agent pipeline. Bypasses the process framework to allow
/// token-level SSE streaming: research → stream write (plain-text prose, no critic loop).
/// </summary>
public sealed class AgentStreamingService(
    IAgentAnswerService agentAnswerService,
    IConversationStore conversationStore,
    IGuardrailsService guardrailsService) : IAgentStreamingService
{
    public async IAsyncEnumerable<StreamEventDto> StreamAsync(
        AgentAskRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = AgentActivitySource.Source.StartActivity("agent.stream");

        // Input guardrails — store any violation outside try/catch so we can yield after.
        var guardrailViolation = await CaptureGuardrailViolationAsync(request.Question, ct);
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
        await conversationStore.AppendAsync(conversationId, new ConversationMessage("user", request.Question));

        var buffer = new System.Text.StringBuilder();
        IReadOnlyList<AgentSource> sources = [];
        var grounded = false;
        await using var agentEvents = agentAnswerService
            .StreamAsync(
                new AgentAnswerRequest(
                    request.Question,
                    topK,
                    history,
                    ConversationSessionId.Create(conversationId)),
                ct)
            .GetAsyncEnumerator(ct);

        while (true)
        {
            AgentStreamEvent? agentEvent = null;
            string? streamError = null;
            try
            {
                if (!await agentEvents.MoveNextAsync())
                {
                    break;
                }

                agentEvent = agentEvents.Current;
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                streamError = exception.Message;
            }

            if (streamError is not null)
            {
                yield return StreamEventDto.ForError(streamError);
                yield break;
            }

            if (agentEvent is null)
            {
                throw new InvalidOperationException("The agent stream advanced without producing an event.");
            }

            switch (agentEvent.Type)
            {
                case "status":
                    yield return StreamEventDto.ForStatus(agentEvent.Message ?? string.Empty);
                    break;
                case "sources":
                    sources = agentEvent.Sources ?? [];
                    activity?.SetTag("rag.sources_count", sources.Count);
                    yield return StreamEventDto.ForSources(sources.Select(SourceMapper.ToDto).ToList());
                    break;
                case "token":
                    var token = agentEvent.Content ?? string.Empty;
                    buffer.Append(token);
                    yield return StreamEventDto.ForToken(token);
                    break;
                case "done":
                    sources = agentEvent.Sources ?? sources;
                    grounded = agentEvent.Grounded ?? sources.Count > 0;
                    break;
                case "error":
                    yield return StreamEventDto.ForError(agentEvent.Error ?? "The agent runtime failed.");
                    yield break;
            }
        }

        var answer = buffer.ToString().Trim();

        // Output guardrail: length limit.
        if (answer.Length > AgentPipelineConstants.MaxAnswerLength)
        {
            answer = answer[..AgentPipelineConstants.MaxAnswerLength] + " … [response truncated]";
        }

        var outputEvaluation = await guardrailsService.ValidateAnswerAsync(answer, sources, ct);
        var persistedAnswer = outputEvaluation.SanitisedOutput ?? answer;
        await conversationStore.AppendAsync(conversationId, new ConversationMessage("assistant", persistedAnswer));

        activity?.SetTag("rag.grounded", grounded);

        yield return StreamEventDto.ForDone(
            conversationId,
            grounded,
            sources.Select(SourceMapper.ToDto).ToList());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs input guardrail checks and returns the violation reason, or null if clean.
    /// Cannot throw inside an async iterator, so we capture the result here instead.
    /// </summary>
    private async Task<string?> CaptureGuardrailViolationAsync(string question, CancellationToken cancellationToken)
    {
        try
        {
            await guardrailsService.ValidateQuestionAsync(question, cancellationToken);
            return null;
        }
        catch (GuardrailException ex)
        {
            return ex.Reason;
        }
    }
}
