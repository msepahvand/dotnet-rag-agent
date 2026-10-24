using System.Text.Json.Serialization;

namespace RagAgent.Core.Models;

public sealed record AgentStreamEvent
{
    public string Type { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentSource>? Sources { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Grounded { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static AgentStreamEvent ForStatus(string message) => new() { Type = "status", Message = message };

    public static AgentStreamEvent ForSources(List<AgentSource> sources) =>
        new() { Type = "sources", Sources = sources };

    public static AgentStreamEvent ForToken(string content) => new() { Type = "token", Content = content };

    public static AgentStreamEvent ForDone(bool grounded, List<AgentSource> sources) =>
        new() { Type = "done", Grounded = grounded, Sources = sources };

    public static AgentStreamEvent ForError(string error) => new() { Type = "error", Error = error };
}
