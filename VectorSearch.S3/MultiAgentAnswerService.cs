using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3.Agents;

namespace VectorSearch.S3;

/// <summary>
/// Implements the Researcher + Writer multi-agent pattern.
/// The researcher retrieves relevant sources; the writer synthesises them into a grounded answer.
/// </summary>
public sealed class MultiAgentAnswerService : IAgentAnswerService
{
    private readonly ResearcherAgent _researcher;
    private readonly WriterAgent _writer;

    public MultiAgentAnswerService(ResearcherAgent researcher, WriterAgent writer)
    {
        _researcher = researcher;
        _writer = writer;
    }

    public async Task<AgentAnswerResult> AnswerAsync(
        string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var normalisedTopK = TopKNormaliser.Normalise(topK);

        var research = await _researcher.ResearchAsync(question, normalisedTopK);
        return await _writer.WriteAsync(question, research, history);
    }
}
