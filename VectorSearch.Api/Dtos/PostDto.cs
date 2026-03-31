using System.Text.Json.Serialization;

namespace VectorSearch.Api.Dtos;

public sealed record PostDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("userId")]
    public int UserId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;
}
