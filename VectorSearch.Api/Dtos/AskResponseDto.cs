namespace VectorSearch.Api.Dtos;

public sealed record AskResponseDto
{
    public string ConversationId { get; init; } = string.Empty;
    public List<string> ToolsUsed { get; init; } = [];
    public bool Grounded { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<CitationDto> Citations { get; init; } = [];
    public List<SourceDto> Sources { get; init; } = [];
}
