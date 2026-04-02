using System.Text.Json;
using VectorSearch.Core.Models;

namespace VectorSearch.S3.Agents;

/// <summary>
/// Responsible solely for retrieval. Calls the search plugin and returns structured sources
/// for the writer agent to synthesise into an answer.
///
/// NOTE: The SK Bedrock connector does not support FunctionChoiceBehavior for Claude models
/// (microsoft/semantic-kernel#9750), so plugins are called directly rather than dispatched
/// via FunctionChoiceBehavior.Required/Auto.
/// </summary>
public sealed class ResearcherAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SemanticSearchPlugin _searchPlugin;

    public ResearcherAgent(SemanticSearchPlugin searchPlugin)
    {
        _searchPlugin = searchPlugin;
    }

    public async Task<ResearchResult> ResearchAsync(string question, int topK)
    {
        var sourcesJson = await _searchPlugin.SearchPostsAsync(question, topK);
        var sources = JsonSerializer.Deserialize<List<AgentSource>>(sourcesJson, JsonOptions) ?? [];

        return new ResearchResult
        {
            Sources = sources,
            SourcesJson = sourcesJson,
            ToolsUsed = ["search_posts"],
        };
    }
}
