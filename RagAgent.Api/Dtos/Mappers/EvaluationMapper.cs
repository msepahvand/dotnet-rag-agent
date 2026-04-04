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
            CitationsValid = r.CitationsValid,
            CitationCount = r.CitationCount,
            Iterations = r.Iterations,
            LatencyMs = r.LatencyMs,
        };
}
