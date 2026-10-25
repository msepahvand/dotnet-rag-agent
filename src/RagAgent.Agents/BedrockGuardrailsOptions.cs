namespace RagAgent.Agents;

public sealed record BedrockGuardrailsOptions(
    string GuardrailIdentifier,
    string GuardrailVersion,
    GuardrailsMode Mode);

public enum GuardrailsMode
{
    Shadow,
    Enforce
}
