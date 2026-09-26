namespace RagAgent.Core.Models;

/// <summary>Per-question result from an evaluation run.</summary>
public sealed record QuestionEvalResult
{
    public string Question { get; init; } = string.Empty;

    /// <summary>Number of sources retrieved for this question.</summary>
    public int RetrievedCount { get; init; }

    /// <summary>Post IDs of the retrieved sources.</summary>
    public IReadOnlyList<int> RetrievedPostIds { get; init; } = [];

    /// <summary>
    /// Whether at least one expected post ID appeared in the retrieved sources.
    /// Null when no expected post IDs were provided for the question.
    /// </summary>
    public bool? HitAtK { get; init; }

    /// <summary>Whether the writer flagged the answer as grounded in the retrieved sources.</summary>
    public bool Grounded { get; init; }

    /// <summary>Independent LLM-judged groundedness score on the evaluator's 1–5 scale.</summary>
    public double? JudgedGroundednessScore { get; init; }

    /// <summary>Independent LLM-judged relevance score on the evaluator's 1–5 scale.</summary>
    public double? JudgedRelevanceScore { get; init; }

    /// <summary>
    /// Whether every cited post ID existed in the retrieved sources (deterministic check).
    /// </summary>
    public bool CitationsValid { get; init; }

    /// <summary>Number of citations the writer produced.</summary>
    public int CitationCount { get; init; }

    /// <summary>Number of writer passes made by the reflection loop.</summary>
    public int Iterations { get; init; }

    /// <summary>End-to-end latency for this question in milliseconds.</summary>
    public double LatencyMs { get; init; }
}

/// <summary>Aggregate report produced by an evaluation run over a question set.</summary>
public sealed record EvaluationReport
{
    public int TotalQuestions { get; init; }

    /// <summary>
    /// Fraction of questions where the answer was grounded (0.0 – 1.0).
    /// </summary>
    public double GroundednessRate { get; init; }

    /// <summary>
    /// Fraction of questions where all citations referenced a retrieved source (0.0 – 1.0).
    /// </summary>
    public double CitationValidityRate { get; init; }

    /// <summary>
    /// Fraction of questions (that had expected post IDs) where Hit@k was satisfied.
    /// Null when no questions had expected post IDs.
    /// </summary>
    public double? HitAtKRate { get; init; }

    /// <summary>Average number of writer iterations across all questions.</summary>
    public double AverageIterations { get; init; }

    /// <summary>Average end-to-end latency per question in milliseconds.</summary>
    public double AverageLatencyMs { get; init; }

    /// <summary>Median end-to-end latency per question in milliseconds.</summary>
    public double? P50LatencyMs { get; init; }

    /// <summary>95th percentile end-to-end latency per question in milliseconds.</summary>
    public double? P95LatencyMs { get; init; }

    /// <summary>Average independent groundedness score on the evaluator's 1–5 scale, if judges ran.</summary>
    public double? AverageJudgedGroundednessScore { get; init; }

    /// <summary>Average independent relevance score on the evaluator's 1–5 scale, if judges ran.</summary>
    public double? AverageJudgedRelevanceScore { get; init; }

    public IReadOnlyList<QuestionEvalResult> Results { get; init; } = [];

    public DateTimeOffset RunAt { get; init; }
}
