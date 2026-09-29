namespace RagAgent.Core;

public sealed record GuardrailEvaluation
{
    public bool Intervened { get; init; }
    public bool Enforced { get; init; }
    public string? SanitisedOutput { get; init; }
    public GuardrailCategory? Category { get; init; }
}
