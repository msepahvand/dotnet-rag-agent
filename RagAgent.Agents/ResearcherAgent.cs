using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

/// <summary>
/// Uses a chat agent to select semantic searches, with a deterministic fallback when it does not
/// request the search tool.
/// </summary>
public sealed class ResearcherAgent(
    IChatClient chatClient,
    SemanticSearchPlugin searchPlugin) : IResearcherAgent
{
    private const string Instructions =
        "You are a research assistant. Find evidence for the user's question by calling the search_posts tool. " +
        "You may search more than once with different focused queries. Do not answer the user; retrieve relevant sources only.";

    public async Task<ResearchResult> ResearchAsync(string question, int topK)
    {
        var normalisedTopK = TopKNormaliser.Normalise(topK);
        var searchTool = new SearchPostsTool(searchPlugin, normalisedTopK);
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "Researcher",
            Description = "Retrieves relevant posts to ground answers.",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [searchTool.Function],
            },
        });

        await agent.RunAsync(question);
        if (!searchTool.WasInvoked)
        {
            await searchTool.SearchPostsAsync(question, normalisedTopK);
        }

        var sources = searchTool.Sources;
        return new ResearchResult
        {
            Sources = sources,
            SourcesJson = JsonSerializer.Serialize(sources, AgentJsonHelpers.JsonOptions),
            ToolsUsed = searchTool.WasInvoked || sources.Count > 0 ? ["search_posts"] : [],
        };
    }

    private sealed class SearchPostsTool(SemanticSearchPlugin searchPlugin, int maxTopK)
    {
        private readonly ConcurrentDictionary<int, AgentSource> _sources = new();
        private int _wasInvoked;

        public AIFunction Function => AIFunctionFactory.Create(
            (Func<string, int, CancellationToken, Task<string>>)SearchPostsAsync,
            name: "search_posts",
            description: "Search indexed posts for relevant evidence. The result count cannot exceed the user's requested limit.");

        public bool WasInvoked => Volatile.Read(ref _wasInvoked) != 0;

        public IReadOnlyList<AgentSource> Sources => _sources.Values
            .OrderBy(source => source.Distance)
            .ThenBy(source => source.PostId)
            .Take(maxTopK)
            .ToArray();

        public async Task<string> SearchPostsAsync(
            [Description("A focused natural-language query for the indexed posts.")] string question,
            [Description("Maximum results requested for this search; the caller's limit is enforced.")] int topK = 5,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _wasInvoked, 1);

            var boundedTopK = Math.Clamp(topK, 1, maxTopK);
            var json = await searchPlugin.SearchPostsAsync(question, boundedTopK);
            var sources = JsonSerializer.Deserialize<List<AgentSource>>(json, AgentJsonHelpers.JsonOptions) ?? [];

            foreach (var source in sources)
            {
                _sources.AddOrUpdate(
                    source.PostId,
                    source,
                    (_, existing) => source.Distance < existing.Distance ? source : existing);
            }

            return json;
        }
    }
}
