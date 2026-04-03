using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3.Agents;

namespace VectorSearch.S3;

/// <summary>
/// Implements the Researcher → Writer → Critic reflection loop.
///
/// The researcher retrieves relevant sources. The writer synthesises them into a grounded answer.
/// The critic then evaluates the answer for relevance, groundedness, and citation validity.
/// If the critic rejects the answer, the writer is re-invoked with the critic's feedback, up to
/// <see cref="MaxIterations"/> times. The final <see cref="AgentAnswerResult.Iterations"/> field
/// records how many write attempts were made.
/// </summary>
public sealed class MultiAgentAnswerService : IAgentAnswerService
{
    private const int MaxIterations = 3;

    private readonly IResearcherAgent _researcher;
    private readonly IWriterAgent _writer;
    private readonly ICriticAgent _critic;

    public MultiAgentAnswerService(
        IResearcherAgent researcher,
        IWriterAgent writer,
        ICriticAgent critic)
    {
        _researcher = researcher;
        _writer = writer;
        _critic = critic;
    }

    public async Task<AgentAnswerResult> AnswerAsync(
        string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var normalisedTopK = TopKNormaliser.Normalise(topK);
        var research = await _researcher.ResearchAsync(question, normalisedTopK);

        AgentAnswerResult answer = default!;
        string? criticFeedback = null;
        var completedIterations = 0;

        for (var i = 0; i < MaxIterations; i++)
        {
            completedIterations = i + 1;
            answer = await _writer.WriteAsync(question, research, history, criticFeedback);

            // Skip critique on the final pass — just return whatever the writer produced
            if (i == MaxIterations - 1)
            {
                break;
            }

            var criticism = await _critic.EvaluateAsync(question, answer, research);
            if (criticism.Approved)
            {
                break;
            }

            criticFeedback = criticism.Feedback;
        }

        return answer with { Iterations = completedIterations };
    }
}
