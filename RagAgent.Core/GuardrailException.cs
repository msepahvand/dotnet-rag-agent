namespace RagAgent.Core;

/// <summary>
/// Thrown when an input or output guardrail check determines that a request
/// or response violates a safety policy.
/// </summary>
public sealed class GuardrailException : Exception
{
    public string Reason { get; }

    public GuardrailException(string reason) : base(reason)
    {
        Reason = reason;
    }
}
