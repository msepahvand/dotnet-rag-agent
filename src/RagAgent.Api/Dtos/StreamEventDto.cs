using System.Text.Json.Serialization;

namespace RagAgent.Api.Dtos;

/// <summary>
/// A single SSE frame sent to the client during a streaming agent response.
/// The <see cref="Type"/> field acts as the event discriminator.
/// </summary>
public sealed record StreamEventDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Human-readable pipeline status update (type = "status").</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    /// <summary>A single LLM token fragment (type = "token").</summary>
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    /// <summary>Retrieved sources emitted after the research phase (type = "sources", "done").</summary>
    [JsonPropertyName("sources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceDto>? Sources { get; init; }

    /// <summary>Final conversation ID (type = "done").</summary>
    [JsonPropertyName("conversationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; init; }

    /// <summary>Whether the final answer is grounded in retrieved sources (type = "done").</summary>
    [JsonPropertyName("grounded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Grounded { get; init; }

    /// <summary>Guardrail or unexpected failure reason (type = "error").</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    // Factory methods — prefixed with "For" to avoid clashing with same-named properties.
    public static StreamEventDto ForStatus(string message) =>
        new() { Type = "status", Message = message };

    public static StreamEventDto ForSources(List<SourceDto> sources) =>
        new() { Type = "sources", Sources = sources };

    public static StreamEventDto ForToken(string content) =>
        new() { Type = "token", Content = content };

    public static StreamEventDto ForDone(string conversationId, bool grounded, List<SourceDto> sources) =>
        new() { Type = "done", ConversationId = conversationId, Grounded = grounded, Sources = sources };

    public static StreamEventDto ForError(string error) =>
        new() { Type = "error", Error = error };
}
