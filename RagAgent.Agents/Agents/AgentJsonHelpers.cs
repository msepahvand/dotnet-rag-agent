using System.Text.Json;
using System.Text.RegularExpressions;

namespace RagAgent.Agents;

/// <summary>
/// Shared JSON utilities used by agent classes that parse LLM output.
/// </summary>
internal static class AgentJsonHelpers
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Extracts a JSON object from raw LLM output, stripping markdown code fences if present.
    /// </summary>
    internal static string ExtractJson(string raw)
    {
        var fenceMatch = Regex.Match(raw, @"```(?:json)?\s*\n?(.*?)\n?\s*```", RegexOptions.Singleline);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups[1].Value.Trim();
        }

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return raw[start..(end + 1)];
        }

        return raw;
    }
}
