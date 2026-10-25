namespace RagAgent.Api.Dtos;

public sealed record ConversationSummaryDto(string ConversationId);

public sealed record ConversationHistoryDto
{
    public string ConversationId { get; init; } = string.Empty;
    public List<ConversationMessageDto> Messages { get; init; } = [];
}

public sealed record ConversationMessageDto(string Role, string Content);
