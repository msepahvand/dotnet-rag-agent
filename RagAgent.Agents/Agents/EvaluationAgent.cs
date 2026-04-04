using System.Diagnostics;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents.Agents;

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

    public EvaluationAgent(IAgentAnswerService answerService)
    {
        _answerService = answerService;
    }

    public async Task<EvaluationReport> EvaluateAsync(
        IReadOnlyList<EvaluationQuestion> questions, int topK = 5)
    {
        var results = new List<QuestionEvalResult>(questions.Count);

        foreach (var q in questions)
        {
            var sw = Stopwatch.StartNew();
            var answer = await _answerService.AnswerAsync(q.Question, topK, []);
            sw.Stop();

            var retrievedIds = answer.Sources.Select(s => s.PostId).ToHashSet();
            var citedIds = answer.Citations.Select(c => c.PostId).ToHashSet();

            var hitAtK = q.ExpectedPostIds.Count == 0
                ? (bool?)null
                : q.ExpectedPostIds.Any(id => retrievedIds.Contains(id));

            var citationsValid = citedIds.Count == 0 || citedIds.All(id => retrievedIds.Contains(id));

            results.Add(new QuestionEvalResult
            {
                Question = q.Question,
                RetrievedCount = answer.Sources.Count,
                RetrievedPostIds = answer.Sources.Select(s => s.PostId).ToList(),
                HitAtK = hitAtK,
                Grounded = answer.Grounded,
                CitationsValid = citationsValid,
                CitationCount = answer.Citations.Count,
                Iterations = answer.Iterations,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
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
            Results = results,
            RunAt = DateTimeOffset.UtcNow,
        };
    }
}
