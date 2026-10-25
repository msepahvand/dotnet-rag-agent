namespace RagAgent.Core.Models;

/// <summary>
/// The verdict produced by the critic agent after evaluating a writer's answer.
/// </summary>
public sealed record CriticResult
{
    public bool Approved { get; init; }
    public string Feedback { get; init; } = string.Empty;
    public IReadOnlyList<string> Checks { get; init; } = [];
}
