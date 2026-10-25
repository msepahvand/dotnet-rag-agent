namespace RagAgent.Api.Dtos;

public sealed record CitationDto
{
    public int PostId { get; init; }
    public string Quote { get; init; } = string.Empty;
}
