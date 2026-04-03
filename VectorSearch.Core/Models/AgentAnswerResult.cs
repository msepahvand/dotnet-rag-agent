namespace VectorSearch.Core.Models;

public record AgentAnswerResult
{
    public string Answer { get; init; } = string.Empty;
    public bool Grounded { get; init; }
    public List<AgentSource> Sources { get; init; } = [];
    public List<Citation> Citations { get; init; } = [];
    public List<string> ToolsUsed { get; init; } = [];
    public int Iterations { get; init; } = 1;
}
