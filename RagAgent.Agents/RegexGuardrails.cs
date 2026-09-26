using System.Text.RegularExpressions;
using RagAgent.Core;

namespace RagAgent.Agents;

public static class RegexGuardrails
{
    private static readonly string[] InjectionPhrases =
    [
        "ignore previous instructions",
        "ignore all previous",
        "disregard your instructions",
        "disregard all previous",
        "you are now",
        "forget your instructions",
        "new instructions:",
        "override instructions",
        "system prompt:",
        "act as if you are",
        "pretend you are",
    ];

    private static readonly Regex EmailPattern =
        new(@"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled);

    private static readonly Regex PhonePattern =
        new(@"\b(\+?[0-9]{1,4}[\s\-.]?)?\(?\d{2,4}\)?[\s\-.]?\d{3,4}[\s\-.]?\d{3,4}\b", RegexOptions.Compiled);

    private static readonly Regex CreditCardPattern =
        new(@"\b\d{4}[\s\-]\d{4}[\s\-]\d{4}[\s\-]\d{4}\b", RegexOptions.Compiled);

    private static readonly string[] OffTopicPhrases =
    [
        "legal advice",
        "medical diagnosis",
        "financial advice",
        "stock tips",
        "investment advice",
        "tax advice",
    ];

    public static void CheckForInjection(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var phrase in InjectionPhrases)
        {
            if (lower.Contains(phrase))
            {
                throw new GuardrailException(
                    $"Prompt injection detected: input contains the disallowed phrase \"{phrase}\".");
            }
        }
    }

    public static void CheckForPii(string text)
    {
        if (EmailPattern.IsMatch(text))
        {
            throw new GuardrailException(
                "Input contains a detected email address. Please remove personal information before submitting.");
        }

        if (CreditCardPattern.IsMatch(text))
        {
            throw new GuardrailException(
                "Input appears to contain a credit card number. Please remove personal information before submitting.");
        }

        if (PhonePattern.IsMatch(text))
        {
            throw new GuardrailException(
                "Input contains a detected phone number. Please remove personal information before submitting.");
        }
    }

    public static void CheckTopicScope(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var phrase in OffTopicPhrases)
        {
            if (lower.Contains(phrase))
            {
                throw new GuardrailException(
                    $"Query is outside the supported topic scope (matched: \"{phrase}\"). " +
                    "This assistant answers questions about posts and articles only.");
            }
        }
    }
}
