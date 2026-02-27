using System.Text.Json.Serialization;

namespace VectorSearch.Core;

public record Post(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("userId")] int UserId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body
);
