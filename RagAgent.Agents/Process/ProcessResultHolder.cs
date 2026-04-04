using RagAgent.Core.Models;

namespace RagAgent.Agents.Process;

/// <summary>
/// Scoped bridge between the fire-and-forget SK Process and the request/response pattern.
/// The <see cref="Steps.OutputStep"/> writes the final result here; <see cref="ProcessAnswerService"/>
/// reads it after <c>StartAsync</c> returns.
/// </summary>
public sealed class ProcessResultHolder
{
    public AgentAnswerResult? Result { get; set; }
}
