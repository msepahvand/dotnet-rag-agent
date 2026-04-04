namespace VectorSearch.Api.Dtos;

public sealed record EvaluationQuestionDto
{
    public required string Question { get; init; }
    public IReadOnlyList<int> ExpectedPostIds { get; init; } = [];
}

public sealed record EvaluationRequestDto
{
    /// <summary>Questions to evaluate. When omitted the default question set is used.</summary>
    public IReadOnlyList<EvaluationQuestionDto>? Questions { get; init; }

    /// <summary>Number of sources to retrieve per question (1–10, default 5).</summary>
    public int TopK { get; init; } = 5;
}

public sealed record QuestionEvalResultDto
{
    public required string Question { get; init; }
    public int RetrievedCount { get; init; }
    public IReadOnlyList<int> RetrievedPostIds { get; init; } = [];
    public bool? HitAtK { get; init; }
    public bool Grounded { get; init; }
    public bool CitationsValid { get; init; }
    public int CitationCount { get; init; }
    public int Iterations { get; init; }
    public double LatencyMs { get; init; }
}

public sealed record EvaluationReportDto
{
    public int TotalQuestions { get; init; }
    public double GroundednessRate { get; init; }
    public double CitationValidityRate { get; init; }
    public double? HitAtKRate { get; init; }
    public double AverageIterations { get; init; }
    public double AverageLatencyMs { get; init; }
    public IReadOnlyList<QuestionEvalResultDto> Results { get; init; } = [];
    public DateTimeOffset RunAt { get; init; }
}
