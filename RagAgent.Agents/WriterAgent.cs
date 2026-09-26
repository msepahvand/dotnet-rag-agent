using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

/// <summary>
/// Responsible solely for synthesis. Receives pre-retrieved sources from the researcher agent
/// and produces a grounded, structured answer with citations.
/// </summary>
public sealed class WriterAgent : IWriterAgent
{
    private const string SystemPrompt =
        "You are a synthesis specialist. You receive pre-retrieved search results and produce a concise, " +
        "grounded answer. You do not call any tools. Answer solely from the provided sources.";

    private const string SynthesisInstruction =
        "Based solely on the search results provided above, respond with a JSON object in this exact shape: " +
        "{\"answer\": \"<your answer>\", \"citations\": [{\"postId\": <N>, \"quote\": \"<excerpt>\"}], \"grounded\": true}. " +
        "Do not use outside knowledge. If the results are insufficient, set grounded to false and explain why in answer.";

    private const string StreamingSystemPrompt =
        "You are a synthesis specialist. Answer questions based solely on the provided search results. " +
        "Be concise and direct. Write your answer as natural prose — do not use JSON or code formatting.";

    private const string StreamingInstruction =
        "Answer the question above based solely on the search results. Write a clear, direct answer in natural prose.";

    private readonly IChatClient _chatClient;

    public WriterAgent(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    // ── Batch (structured JSON) ───────────────────────────────────────────────
    public async Task<AgentAnswerResult> WriteAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ConversationMessage> history,
        string? criticFeedback = null)
    {
        var chatHistory = BuildBatchChatHistory(question, research, history, criticFeedback);

        var options = new ChatOptions
        {
            MaxOutputTokens = 2048,
        };

        var response = await _chatClient.GetResponseAsync(chatHistory, options);
        var rawOutput = response.Text?.Trim() ?? string.Empty;

        var fallback = BuildDeterministicAnswer(question, research.Sources);
        return ParseStructuredAnswer(rawOutput, research.Sources, research.ToolsUsed, fallback);
    }

    // ── Streaming (plain-text prose) ─────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ConversationMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatHistory = BuildStreamingChatHistory(question, research, history);

        var options = new ChatOptions
        {
            MaxOutputTokens = 2048,
        };

        await foreach (var chunk in _chatClient.GetStreamingResponseAsync(
            chatHistory, options, ct))
        {
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                yield return chunk.Text;
            }
        }
    }

    // ── Chat history builders ─────────────────────────────────────────────────
    private static List<ChatMessage> BuildBatchChatHistory(
        string question,
        ResearchResult research,
        IReadOnlyList<ConversationMessage> history,
        string? criticFeedback)
    {
        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
        };
        AddConversationHistory(chatHistory, history);

        chatHistory.Add(new ChatMessage(ChatRole.User, question));
        chatHistory.Add(new ChatMessage(ChatRole.User, $"Search results:\n{research.SourcesJson}"));

        if (!string.IsNullOrWhiteSpace(criticFeedback))
        {
            chatHistory.Add(new ChatMessage(
                ChatRole.User,
                $"A critic reviewed a previous draft of this answer and identified the following issues: " +
                $"{criticFeedback} Please address these issues in your revised answer."));
        }

        chatHistory.Add(new ChatMessage(ChatRole.User, SynthesisInstruction));
        return chatHistory;
    }

    private static List<ChatMessage> BuildStreamingChatHistory(
        string question,
        ResearchResult research,
        IReadOnlyList<ConversationMessage> history)
    {
        var chatHistory = new List<ChatMessage>
        {
            new(ChatRole.System, StreamingSystemPrompt),
        };
        AddConversationHistory(chatHistory, history);

        chatHistory.Add(new ChatMessage(ChatRole.User, question));
        chatHistory.Add(new ChatMessage(ChatRole.User, $"Search results:\n{research.SourcesJson}"));
        chatHistory.Add(new ChatMessage(ChatRole.User, StreamingInstruction));
        return chatHistory;
    }

    private static void AddConversationHistory(
        List<ChatMessage> chatHistory,
        IReadOnlyList<ConversationMessage> history)
    {
        foreach (var msg in history)
        {
            var role = msg.Role switch
            {
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User,
            };
            chatHistory.Add(new ChatMessage(role, msg.Content));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static AgentAnswerResult ParseStructuredAnswer(
        string rawOutput,
        IReadOnlyList<AgentSource> sources,
        IReadOnlyList<string> toolsUsed,
        string fallback)
    {
        var json = AgentJsonHelpers.ExtractJson(rawOutput);
        try
        {
            var structured = JsonSerializer.Deserialize<StructuredLlmAnswer>(json, AgentJsonHelpers.JsonOptions);
            if (structured != null && !string.IsNullOrWhiteSpace(structured.Answer))
            {
                return new AgentAnswerResult
                {
                    Answer = structured.Answer,
                    Grounded = structured.Grounded,
                    Sources = sources.ToList(),
                    Citations = structured.Citations ?? [],
                    ToolsUsed = toolsUsed.ToList(),
                };
            }
        }
        catch (JsonException) { }

        return new AgentAnswerResult
        {
            Answer = string.IsNullOrWhiteSpace(rawOutput) ? fallback : rawOutput,
            Grounded = false,
            Sources = sources.ToList(),
            Citations = [],
            ToolsUsed = toolsUsed.ToList(),
        };
    }

    private static string BuildDeterministicAnswer(string question, IReadOnlyList<AgentSource> sources)
    {
        var evidence = string.Join("\n", sources.Take(3).Select((s, i) =>
            $"{i + 1}. {s.Title} [PostId: {s.PostId}] - {s.Snippet}"));
        return $"Grounded answer for: {question}\n\nSupporting evidence:\n{evidence}";
    }

    private sealed record StructuredLlmAnswer
    {
        [JsonPropertyName("answer")] public string Answer { get; init; } = string.Empty;
        [JsonPropertyName("citations")] public List<Citation> Citations { get; init; } = [];
        [JsonPropertyName("grounded")] public bool Grounded { get; init; }
    }
}
