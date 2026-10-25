namespace RagAgent.Core;

/// <summary>
/// Thrown when an input or output guardrail check determines that a request
/// or response violates a safety policy.
/// </summary>
public sealed class GuardrailException : Exception
{
    public string Reason { get; }
    public GuardrailCategory Category { get; }

    public GuardrailException(string reason, GuardrailCategory category = GuardrailCategory.Injection) : base(reason)
    {
        Reason = reason;
        Category = category;
    }
}

public enum GuardrailCategory
{
    Injection,
    Pii,
    Topic
}
