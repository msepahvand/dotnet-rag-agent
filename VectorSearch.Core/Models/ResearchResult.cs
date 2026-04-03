namespace VectorSearch.Core.Models;

/// <summary>
/// The output produced by the researcher agent and consumed by the writer agent.
/// Contains retrieved sources and the raw JSON representation for injection into the writer's context.
/// </summary>
public sealed record ResearchResult
{
    public IReadOnlyList<AgentSource> Sources { get; init; } = [];
    public string SourcesJson { get; init; } = string.Empty;
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
}
