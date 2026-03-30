namespace VectorSearch.Core;

public record AgentAnswerResult
{
    public string Answer { get; init; } = string.Empty;
    public List<AgentSource> Sources { get; init; } = [];
    public List<Citation> Citations { get; init; } = [];
}
