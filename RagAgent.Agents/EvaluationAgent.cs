using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

/// <summary>
/// Runs a question set through the full RAG pipeline and aggregates quality metrics.
///
/// Metrics computed per question:
/// - <b>Retrieved</b>: number of sources returned by the retrieval step.
/// - <b>Grounded</b>: whether the writer flagged its answer as grounded.
/// - <b>CitationsValid</b>: whether every cited postId exists in the retrieved sources
///   (deterministic check — no LLM required).
/// - <b>HitAtK</b>: whether any expected postId appeared in the retrieved sources
///   (only computed when <see cref="EvaluationQuestion.ExpectedPostIds"/> is non-empty).
/// </summary>
public sealed class EvaluationAgent : IEvaluationAgent
{
    private readonly IAgentAnswerService _answerService;
    private readonly IAnswerQualityJudge? _qualityJudge;
    private readonly TimeProvider _timeProvider;

    public EvaluationAgent(
        IAgentAnswerService answerService,
        IAnswerQualityJudge? qualityJudge = null,
        TimeProvider? timeProvider = null)
    {
        _answerService = answerService;
        _qualityJudge = qualityJudge;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<EvaluationReport> EvaluateAsync(
        IReadOnlyList<EvaluationQuestion> questions, int topK = 5)
    {
        var results = new List<QuestionEvalResult>(questions.Count);

        foreach (var q in questions)
        {
            var startedAt = _timeProvider.GetTimestamp();
            var answer = await _answerService.AnswerAsync(q.Question, topK, []);
            var latencyMs = _timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

            var retrievedIds = answer.Sources.Select(s => s.PostId).ToHashSet();
            var citedIds = answer.Citations.Select(c => c.PostId).ToHashSet();

            var hitAtK = q.ExpectedPostIds.Count == 0
                ? (bool?)null
                : q.ExpectedPostIds.Any(id => retrievedIds.Contains(id));

            var citationsValid = citedIds.Count == 0 || citedIds.All(id => retrievedIds.Contains(id));
            var judgeScores = _qualityJudge is null
                ? null
                : await _qualityJudge.EvaluateAsync(q.Question, answer.Answer, answer.Sources);

            results.Add(new QuestionEvalResult
            {
                Question = q.Question,
                RetrievedCount = answer.Sources.Count,
                RetrievedPostIds = answer.Sources.Select(s => s.PostId).ToList(),
                HitAtK = hitAtK,
                Grounded = answer.Grounded,
                JudgedGroundednessScore = judgeScores?.Groundedness,
                JudgedRelevanceScore = judgeScores?.Relevance,
                CitationsValid = citationsValid,
                CitationCount = answer.Citations.Count,
                Iterations = answer.Iterations,
                LatencyMs = latencyMs,
            });
        }

        return BuildReport(results);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static EvaluationReport BuildReport(IReadOnlyList<QuestionEvalResult> results)
    {
        if (results.Count == 0)
        {
            return new EvaluationReport { RunAt = DateTimeOffset.UtcNow };
        }

        var withExpected = results.Where(r => r.HitAtK.HasValue).ToList();
        var latencies = results.Select(r => r.LatencyMs).Order().ToArray();
        var judgedGroundedness = results.Where(r => r.JudgedGroundednessScore.HasValue)
            .Select(r => r.JudgedGroundednessScore!.Value);
        var judgedRelevance = results.Where(r => r.JudgedRelevanceScore.HasValue)
            .Select(r => r.JudgedRelevanceScore!.Value);

        return new EvaluationReport
        {
            TotalQuestions = results.Count,
            GroundednessRate = results.Count(r => r.Grounded) / (double)results.Count,
            CitationValidityRate = results.Count(r => r.CitationsValid) / (double)results.Count,
            HitAtKRate = withExpected.Count > 0
                ? withExpected.Count(r => r.HitAtK == true) / (double)withExpected.Count
                : null,
            AverageIterations = results.Average(r => r.Iterations),
            AverageLatencyMs = results.Average(r => r.LatencyMs),
            P50LatencyMs = Percentile(latencies, 0.50),
            P95LatencyMs = Percentile(latencies, 0.95),
            AverageJudgedGroundednessScore = AverageOrNull(judgedGroundedness),
            AverageJudgedRelevanceScore = AverageOrNull(judgedRelevance),
            Results = results,
            RunAt = DateTimeOffset.UtcNow,
        };
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var position = (sortedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        var fraction = position - lowerIndex;

        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }

    private static double? AverageOrNull(IEnumerable<double> values)
    {
        var scores = values.ToList();
        return scores.Count == 0 ? null : scores.Average();
    }
}
