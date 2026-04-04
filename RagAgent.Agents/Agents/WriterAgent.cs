using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Amazon;
using RagAgent.Core.Models;

namespace RagAgent.Agents.Agents;

/// <summary>
/// Responsible solely for synthesis. Receives pre-retrieved sources from the researcher agent
/// and produces a grounded, structured answer with citations.
/// </summary>
public sealed class WriterAgent : IWriterAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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

    private readonly IChatCompletionService _chatService;
    private readonly Kernel _kernel;

    public WriterAgent(IChatCompletionService chatService, Kernel kernel)
    {
        _chatService = chatService;
        _kernel = kernel;
    }

    // ── Batch (structured JSON) ───────────────────────────────────────────────
    public async Task<AgentAnswerResult> WriteAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        string? criticFeedback = null)
    {
        var chatHistory = BuildBatchChatHistory(question, research, history, criticFeedback);

        var settings = new AmazonClaudeExecutionSettings
        {
            MaxTokensToSample = 2048,
            FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
        };

        var response = await _chatService.GetChatMessageContentsAsync(chatHistory, settings, _kernel);
        var rawOutput = response.FirstOrDefault()?.Content?.Trim() ?? string.Empty;

        var fallback = BuildDeterministicAnswer(question, research.Sources);
        return ParseStructuredAnswer(rawOutput, research.Sources, research.ToolsUsed, fallback);
    }

    // ── Streaming (plain-text prose) ─────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatHistory = BuildStreamingChatHistory(question, research, history);

        var settings = new AmazonClaudeExecutionSettings
        {
            MaxTokensToSample = 2048,
            FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
        };

        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
            chatHistory, settings, _kernel, ct))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                yield return chunk.Content;
            }
        }
    }

    // ── Chat history builders ─────────────────────────────────────────────────
    private static ChatHistory BuildBatchChatHistory(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history,
        string? criticFeedback)
    {
        var chatHistory = new ChatHistory(SystemPrompt);
        AddConversationHistory(chatHistory, history);

        chatHistory.AddUserMessage(question);
        chatHistory.AddUserMessage($"Search results:\n{research.SourcesJson}");

        if (!string.IsNullOrWhiteSpace(criticFeedback))
        {
            chatHistory.AddUserMessage(
                $"A critic reviewed a previous draft of this answer and identified the following issues: " +
                $"{criticFeedback} Please address these issues in your revised answer.");
        }

        chatHistory.AddUserMessage(SynthesisInstruction);
        return chatHistory;
    }

    private static ChatHistory BuildStreamingChatHistory(
        string question,
        ResearchResult research,
        IReadOnlyList<ChatMessage> history)
    {
        var chatHistory = new ChatHistory(StreamingSystemPrompt);
        AddConversationHistory(chatHistory, history);

        chatHistory.AddUserMessage(question);
        chatHistory.AddUserMessage($"Search results:\n{research.SourcesJson}");
        chatHistory.AddUserMessage(StreamingInstruction);
        return chatHistory;
    }

    private static void AddConversationHistory(ChatHistory chatHistory, IReadOnlyList<ChatMessage> history)
    {
        foreach (var msg in history)
        {
            var role = msg.Role switch
            {
                "assistant" => AuthorRole.Assistant,
                "system" => AuthorRole.System,
                _ => AuthorRole.User,
            };
            chatHistory.Add(new ChatMessageContent(role, msg.Content));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static AgentAnswerResult ParseStructuredAnswer(
        string rawOutput,
        IReadOnlyList<AgentSource> sources,
        IReadOnlyList<string> toolsUsed,
        string fallback)
    {
        var json = ExtractJson(rawOutput);
        try
        {
            var structured = JsonSerializer.Deserialize<StructuredLlmAnswer>(json, JsonOptions);
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

    private static string ExtractJson(string raw)
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

    private sealed record StructuredLlmAnswer
    {
        [JsonPropertyName("answer")] public string Answer { get; init; } = string.Empty;
        [JsonPropertyName("citations")] public List<Citation> Citations { get; init; } = [];
        [JsonPropertyName("grounded")] public bool Grounded { get; init; }
    }
}
