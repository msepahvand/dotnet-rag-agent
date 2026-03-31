namespace VectorSearch.Api.Dtos;

public sealed record SearchResultDto
{
    public int PostId { get; init; }
    public string Title { get; init; } = string.Empty;
    public double Distance { get; init; }
    public int UserId { get; init; }
}
