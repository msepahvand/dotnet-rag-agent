namespace RagAgent.Core.Models;

/// <summary>
/// A single question in an evaluation run. <see cref="ExpectedPostIds"/> is optional:
/// when provided, Hit@k is computed; when empty, only groundedness and citation
/// validity are scored.
/// </summary>
public sealed record EvaluationQuestion(
    string Question,
    IReadOnlyList<int> ExpectedPostIds);
