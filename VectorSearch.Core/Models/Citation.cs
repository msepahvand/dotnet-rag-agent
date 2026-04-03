using System.Text.Json.Serialization;

namespace VectorSearch.Core.Models;

public record Citation
{
    [JsonPropertyName("postId")]
    public int PostId { get; init; }

    [JsonPropertyName("quote")]
    public string Quote { get; init; } = string.Empty;
}
