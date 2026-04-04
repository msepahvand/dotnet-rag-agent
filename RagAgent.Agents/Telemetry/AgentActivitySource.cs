using System.Diagnostics;

namespace RagAgent.Agents.Telemetry;

/// <summary>
/// Central <see cref="ActivitySource"/> for the RAG agent pipeline.
/// Add <c>RagAgent</c> to your tracer provider to receive these spans.
/// </summary>
public static class AgentActivitySource
{
    /// <summary>Activity source name — register with <c>AddSource(AgentActivitySource.Name)</c>.</summary>
    public const string Name = "RagAgent";

    /// <summary>The shared source instance used throughout the agent pipeline.</summary>
    public static readonly ActivitySource Source = new(Name);
}
