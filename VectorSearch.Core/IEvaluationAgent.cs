using VectorSearch.Core.Models;

namespace VectorSearch.Core;

public interface IEvaluationAgent
{
    Task<EvaluationReport> EvaluateAsync(IReadOnlyList<EvaluationQuestion> questions, int topK = 5);
}
