using RagAgent.Core.Models;

namespace RagAgent.Agents;

public interface IAnswerQualityJudge
{
    Task<AnswerQualityScores> EvaluateAsync(
        string question,
        string answer,
        IReadOnlyList<AgentSource> sources);
}

public sealed record AnswerQualityScores(double? Groundedness, double? Relevance);
