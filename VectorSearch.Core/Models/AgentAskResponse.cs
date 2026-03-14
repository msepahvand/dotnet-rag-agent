namespace VectorSearch.Core;

public record AgentAskResponse
{
    public string ToolUsed { get; init; } = "semantic-search";
    public bool Grounded { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<AgentSource> Sources { get; init; } = [];
}
