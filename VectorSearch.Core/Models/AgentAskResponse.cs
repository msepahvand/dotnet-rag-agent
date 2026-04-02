namespace VectorSearch.Core.Models;

public record AgentAskResponse
{
    public string ConversationId { get; init; } = string.Empty;
    public List<string> ToolsUsed { get; init; } = [];
    public bool Grounded { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<Citation> Citations { get; init; } = [];
    public List<AgentSource> Sources { get; init; } = [];
}
