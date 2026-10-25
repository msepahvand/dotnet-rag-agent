using System.Diagnostics.Metrics;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

public sealed class BedrockGuardrailsService(
    GuardrailsService regexGuardrails,
    IBedrockGuardrailClient bedrockClient,
    BedrockGuardrailsOptions options,
    ILogger<BedrockGuardrailsService> logger) : IGuardrailsService
{
    public const string MeterName = "RagAgent.Guardrails";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ShadowAgreement = Meter.CreateCounter<long>("guardrail.shadow.agree");
    private static readonly Counter<long> ShadowDisagreement = Meter.CreateCounter<long>("guardrail.shadow.disagree");
    private static readonly Counter<long> BedrockFailures = Meter.CreateCounter<long>("guardrail.bedrock.failure");
    private static readonly Counter<long> OutputInterventions = Meter.CreateCounter<long>("guardrail.output.intervention");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    public async Task ValidateQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        GuardrailException? regexViolation = null;
        try
        {
            await regexGuardrails.ValidateQuestionAsync(question, cancellationToken);
        }
        catch (GuardrailException exception)
        {
            regexViolation = exception;
        }

        var bedrockResult = await TryApplyAsync(CreateInputRequest(question), "question", cancellationToken);
        if (options.Mode == GuardrailsMode.Shadow && bedrockResult is not null)
        {
            RecordShadowDecision("question", regexViolation is not null, bedrockResult.Intervened);
            if ((regexViolation is not null) != bedrockResult.Intervened)
            {
                logger.LogInformation(
                    "Bedrock guardrail shadow decision differed from regex for {Direction}: regexIntervened={RegexIntervened}, bedrockIntervened={BedrockIntervened}.",
                    "question",
                    regexViolation is not null,
                    bedrockResult.Intervened);
            }
        }

        if (regexViolation is not null)
        {
            throw regexViolation;
        }

        if (options.Mode == GuardrailsMode.Enforce && bedrockResult?.Intervened == true)
        {
            throw new GuardrailException(
                "Question was blocked by safety policies.",
                bedrockResult.Category ?? GuardrailCategory.Injection);
        }
    }

    public async Task<GuardrailEvaluation> ValidateAnswerAsync(
        string answer,
        IReadOnlyList<AgentSource> sources,
        CancellationToken cancellationToken = default)
    {
        var result = await TryApplyAsync(CreateOutputRequest(answer, sources), "answer", cancellationToken);
        if (result?.Intervened == true)
        {
            OutputInterventions.Add(1, new KeyValuePair<string, object?>("mode", options.Mode.ToString()));
        }

        return new GuardrailEvaluation
        {
            Intervened = result?.Intervened == true,
            Enforced = options.Mode == GuardrailsMode.Enforce,
            SanitisedOutput = result?.OutputText,
            Category = result?.Category
        };
    }

    private ApplyGuardrailRequest CreateInputRequest(string question) => new()
    {
        GuardrailIdentifier = options.GuardrailIdentifier,
        GuardrailVersion = options.GuardrailVersion,
        Source = "INPUT",
        Content =
        [
            new GuardrailContentBlock
            {
                Text = new GuardrailTextBlock { Text = question }
            }
        ]
    };

    private ApplyGuardrailRequest CreateOutputRequest(string answer, IReadOnlyList<AgentSource> sources)
    {
        var content = new List<GuardrailContentBlock>
        {
            new()
            {
                Text = new GuardrailTextBlock
                {
                    Text = answer,
                    Qualifiers = ["guard_content"]
                }
            }
        };

        content.AddRange(sources.Select(source => new GuardrailContentBlock
        {
            Text = new GuardrailTextBlock
            {
                Text = $"{source.Title}\n{source.Snippet}",
                Qualifiers = ["grounding_source"]
            }
        }));

        return new ApplyGuardrailRequest
        {
            GuardrailIdentifier = options.GuardrailIdentifier,
            GuardrailVersion = options.GuardrailVersion,
            Source = "OUTPUT",
            Content = content
        };
    }

    private async Task<GuardrailResult?> TryApplyAsync(
        ApplyGuardrailRequest request,
        string direction,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        ApplyGuardrailResponse response;
        try
        {
            response = await bedrockClient.ApplyGuardrailAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            BedrockFailures.Add(1, new KeyValuePair<string, object?>("direction", direction));
            logger.LogWarning(exception, "Bedrock guardrail check failed for {Direction}; retaining regex and deterministic safety checks.", direction);
            return null;
        }

        var intervened = string.Equals(response.Action, "GUARDRAIL_INTERVENED", StringComparison.Ordinal);
        var category = GetCategory(response.Assessments);
        if (intervened && direction == "answer")
        {
            logger.LogInformation("Bedrock guardrail intervened on final answer with category {Category}.", category);
        }
        else if (direction == "answer")
        {
            logger.LogDebug("Bedrock guardrail approved the final answer.");
        }

        return new GuardrailResult(
            intervened,
            response.Outputs?.FirstOrDefault()?.Text,
            category);
    }

    private static GuardrailCategory? GetCategory(List<GuardrailAssessment>? assessments)
    {
        if (assessments is null || assessments.Count == 0)
        {
            return null;
        }

        if (assessments.Any(assessment =>
                assessment.SensitiveInformationPolicy?.PiiEntities?.Any(entity => entity.Detected == true) == true))
        {
            return GuardrailCategory.Pii;
        }

        if (assessments.Any(assessment =>
                assessment.TopicPolicy?.Topics?.Any(topic => topic.Detected == true) == true ||
                assessment.WordPolicy?.CustomWords?.Any(word => word.Detected == true) == true))
        {
            return GuardrailCategory.Topic;
        }

        if (assessments.Any(assessment => assessment.ContentPolicy?.Filters?
                .Any(filter => filter.Detected == true &&
                               string.Equals(filter.Type, "PROMPT_ATTACK", StringComparison.Ordinal)) == true))
        {
            return GuardrailCategory.Injection;
        }

        return GuardrailCategory.Injection;
    }

    private static void RecordShadowDecision(string direction, bool regexIntervened, bool bedrockIntervened)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("direction", direction)
        };

        if (regexIntervened == bedrockIntervened)
        {
            ShadowAgreement.Add(1, tags);
        }
        else
        {
            ShadowDisagreement.Add(1, tags);
        }
    }

    private sealed record GuardrailResult(
        bool Intervened,
        string? OutputText,
        GuardrailCategory? Category);
}
