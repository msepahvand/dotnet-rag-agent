using RagAgent.Core.Models;

namespace RagAgent.Api.Dtos.Mappers;

public static class EvaluationMapper
{
    public static EvaluationReportDto ToDto(EvaluationReport model) =>
        new()
        {
            TotalQuestions = model.TotalQuestions,
            GroundednessRate = model.GroundednessRate,
            CitationValidityRate = model.CitationValidityRate,
            HitAtKRate = model.HitAtKRate,
            AverageIterations = model.AverageIterations,
            AverageLatencyMs = model.AverageLatencyMs,
            P50LatencyMs = model.P50LatencyMs,
            P95LatencyMs = model.P95LatencyMs,
            AverageJudgedGroundednessScore = model.AverageJudgedGroundednessScore,
            AverageJudgedRelevanceScore = model.AverageJudgedRelevanceScore,
            Results = model.Results.Select(ToDto).ToList(),
            RunAt = model.RunAt,
        };

    private static QuestionEvalResultDto ToDto(QuestionEvalResult r) =>
        new()
        {
            Question = r.Question,
            RetrievedCount = r.RetrievedCount,
            RetrievedPostIds = r.RetrievedPostIds,
            HitAtK = r.HitAtK,
            Grounded = r.Grounded,
            JudgedGroundednessScore = r.JudgedGroundednessScore,
            JudgedRelevanceScore = r.JudgedRelevanceScore,
            CitationsValid = r.CitationsValid,
            CitationCount = r.CitationCount,
            Iterations = r.Iterations,
            LatencyMs = r.LatencyMs,
        };
}
