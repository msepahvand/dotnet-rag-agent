namespace VectorSearch.Core;

public record AgentAnswerResult
{
    public string Answer { get; init; } = string.Empty;
    public bool Grounded { get; init; }
    public List<AgentSource> Sources { get; init; } = [];
    public List<Citation> Citations { get; init; } = [];
}
