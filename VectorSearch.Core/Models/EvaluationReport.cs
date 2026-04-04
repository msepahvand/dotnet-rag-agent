namespace VectorSearch.Core.Models;

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

    public IReadOnlyList<QuestionEvalResult> Results { get; init; } = [];

    public DateTimeOffset RunAt { get; init; }
}
