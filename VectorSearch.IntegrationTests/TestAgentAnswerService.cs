using VectorSearch.Core;

namespace VectorSearch.IntegrationTests;

internal sealed class TestAgentAnswerService : IAgentAnswerService
{
    public Task<string> BuildGroundedAnswerAsync(string question, List<AgentSource> sources)
    {
        if (sources.Count == 0)
        {
            return Task.FromResult("I couldn't find enough grounded sources.");
        }

        var citations = string.Join(", ", sources.Take(3).Select(source => $"[PostId: {source.PostId}]"));
        return Task.FromResult($"Grounded test answer for '{question}' using {citations}.");
    }
}
