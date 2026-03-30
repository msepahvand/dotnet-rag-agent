using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace VectorSearch.S3;

public sealed class ToolInvocationFilter(ILogger<ToolInvocationFilter> logger) : IFunctionInvocationFilter
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 10;

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        NormalizeTopK(context.Arguments);

        var pluginName = string.IsNullOrWhiteSpace(context.Function.PluginName)
            ? "unknown"
            : context.Function.PluginName;
        var functionName = context.Function.Name;

        logger.LogInformation("Tool call started: {Plugin}.{Function}", pluginName, functionName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
            stopwatch.Stop();

            var outcome = context.Result is null ? "no-result" : "success";
            logger.LogInformation(
                "Tool call completed: {Plugin}.{Function} in {DurationMs}ms ({Outcome})",
                pluginName,
                functionName,
                stopwatch.ElapsedMilliseconds,
                outcome);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "Tool call failed: {Plugin}.{Function} after {DurationMs}ms",
                pluginName,
                functionName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static void NormalizeTopK(KernelArguments arguments)
    {
        if (!arguments.TryGetValue("topK", out var rawValue) || rawValue is null)
        {
            return;
        }

        if (!TryParseInt(rawValue, out var parsedTopK))
        {
            arguments["topK"] = DefaultTopK;
            return;
        }

        var normalized = parsedTopK <= 0 ? DefaultTopK : Math.Min(parsedTopK, MaxTopK);
        arguments["topK"] = normalized;
    }

    private static bool TryParseInt(object value, out int parsed)
    {
        switch (value)
        {
            case int intValue:
                parsed = intValue;
                return true;
            case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                parsed = (int)longValue;
                return true;
            case string stringValue when int.TryParse(stringValue, out var result):
                parsed = result;
                return true;
            default:
                parsed = 0;
                return false;
        }
    }
}
