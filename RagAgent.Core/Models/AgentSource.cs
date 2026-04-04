namespace RagAgent.Core.Models;

public record AgentSource
{
    public int PostId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public double Distance { get; init; }
}
