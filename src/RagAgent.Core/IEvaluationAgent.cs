using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IEvaluationAgent
{
    Task<EvaluationReport> EvaluateAsync(IReadOnlyList<EvaluationQuestion> questions, int topK = 5);
}
