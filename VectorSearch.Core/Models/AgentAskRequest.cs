namespace VectorSearch.Core.Models;

public record AgentAskRequest
{
    public string Question { get; init; } = string.Empty;
    public int TopK { get; init; } = 5;
    public string? ConversationId { get; init; }
}
