namespace VectorSearch.Api.Dtos;

public sealed record SourceDto
{
    public int PostId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public double Distance { get; init; }
}
