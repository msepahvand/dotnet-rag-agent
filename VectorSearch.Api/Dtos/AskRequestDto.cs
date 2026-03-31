namespace VectorSearch.Api.Dtos;

public sealed record AskRequestDto
{
    public string Question { get; init; } = string.Empty;
    public int TopK { get; init; } = 5;
}
