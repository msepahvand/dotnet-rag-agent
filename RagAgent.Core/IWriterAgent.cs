using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IWriterAgent
{
    Task<AgentAnswerResult> WriteAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        string? criticFeedback = null);

    /// <summary>
    /// Streams the answer token-by-token using a plain-text prompt (no JSON wrapper).
    /// Intended for SSE endpoints where raw streaming UX matters more than structured citations.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}
