namespace RagAgent.Core.Models;

public record SearchResult
{
    public double Distance { get; init; }
    public string Title { get; init; } = string.Empty;
    public int PostId { get; init; }
    public int UserId { get; init; }
}
