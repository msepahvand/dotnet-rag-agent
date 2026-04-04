using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace RagAgent.Agents.Filters;

/// <summary>
/// SK <see cref="IFunctionInvocationFilter"/> that runs output guardrail checks after each
/// tool invocation (e.g. SemanticSearchPlugin). Logs warnings for oversized results and
/// potentially harmful content detected in tool outputs. Does not throw — tool results are
/// internal; the answer-level validation in AgentOrchestrationService is the final gate.
/// </summary>
public sealed class OutputGuardrailFilter(ILogger<OutputGuardrailFilter> logger) : IFunctionInvocationFilter
{
    private const int MaxToolResultLength = 10_000;

    private static readonly string[] HarmfulTerms =
    [
        "jailbreak",
        "bypass safety",
        "ignore safety",
        "bypass filter",
    ];

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        await next(context);

        var result = context.Result?.ToString() ?? string.Empty;
        var pluginName = context.Function.PluginName ?? "unknown";
        var functionName = context.Function.Name;

        if (result.Length > MaxToolResultLength)
        {
            logger.LogWarning(
                "Tool result from {Plugin}.{Function} is {Length} characters, which exceeds the " +
                "{Max}-character limit. The result may be truncated downstream.",
                pluginName,
                functionName,
                result.Length,
                MaxToolResultLength);
        }

        var lower = result.ToLowerInvariant();
        foreach (var term in HarmfulTerms)
        {
            if (lower.Contains(term))
            {
                logger.LogWarning(
                    "Potential harmful content detected in output of {Plugin}.{Function}: " +
                    "matched term \"{Term}\". Review the tool result before surfacing to the user.",
                    pluginName,
                    functionName,
                    term);
            }
        }
    }
}
