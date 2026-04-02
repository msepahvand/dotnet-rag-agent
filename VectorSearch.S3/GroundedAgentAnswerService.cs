using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Amazon;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService : IAgentAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string SynthesisPrompt =
        "Based solely on the search results provided above, respond with a JSON object in this exact shape: " +
        "{\"answer\": \"<your answer>\", \"citations\": [{\"postId\": <N>, \"quote\": \"<excerpt>\"}], \"grounded\": true}. " +
        "Do not use outside knowledge. If the results are insufficient, set grounded to false and explain why in answer.";

    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly SemanticSearchPlugin _semanticSearchPlugin;

    public GroundedAgentAnswerService(
        Kernel kernel,
        SemanticSearchPlugin semanticSearchPlugin,
        IChatCompletionService chatCompletionService)
    {
        _kernel = kernel;
        _chatCompletionService = chatCompletionService;
        _semanticSearchPlugin = semanticSearchPlugin;
    }

    public async Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var normalizedTopK = topK <= 0 ? 5 : Math.Min(topK, 10);
        return await TwoPassAnswerAsync(question, normalizedTopK, history);
    }

    private async Task<AgentAnswerResult> TwoPassAnswerAsync(
        string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var chatHistory = new ChatHistory(
            "You are a grounded research assistant. Use the available tools to gather information " +
            "before answering. Always call at least one tool.");

        foreach (var msg in history)
        {
            var role = msg.Role switch
            {
                "assistant" => AuthorRole.Assistant,
                "system" => AuthorRole.System,
                _ => AuthorRole.User
            };
            chatHistory.Add(new ChatMessageContent(role, msg.Content));
        }

        chatHistory.AddUserMessage(question);

        // Retrieval — call the search plugin directly; the SK Bedrock connector does not support
        // function calling (https://github.com/microsoft/semantic-kernel/issues/9750), so we
        // cannot rely on FunctionChoiceBehavior to dispatch tools at runtime.
        var searchResultJson = await _semanticSearchPlugin.SearchPostsAsync(question, topK);
        var sources = JsonSerializer.Deserialize<List<AgentSource>>(searchResultJson, JsonOptions) ?? [];
        chatHistory.AddUserMessage($"Search results:\n{searchResultJson}");

        // Synthesis — ask the LLM to produce a grounded structured answer from the results above
        chatHistory.AddUserMessage(SynthesisPrompt);
        var synthesisSettings = new AmazonClaudeExecutionSettings
        {
            MaxTokensToSample = 4096,
            FunctionChoiceBehavior = FunctionChoiceBehavior.None()
        };
        var synthesis = await _chatCompletionService.GetChatMessageContentsAsync(
            chatHistory, synthesisSettings, _kernel);
        var rawOutput = synthesis.FirstOrDefault()?.Content?.Trim() ?? "";

        var deterministicAnswer = BuildDeterministicAnswer(question, sources);
        return ParseStructuredAnswer(rawOutput, sources, ["search_posts"], deterministicAnswer);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static AgentAnswerResult ParseStructuredAnswer(
        string rawOutput, List<AgentSource> sources, List<string> toolsUsed, string fallback)
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
                    Sources = sources,
                    Citations = structured.Citations ?? [],
                    ToolsUsed = toolsUsed
                };
            }
        }
        catch (JsonException) { }

        return new AgentAnswerResult
        {
            Answer = string.IsNullOrWhiteSpace(rawOutput) ? fallback : rawOutput,
            Grounded = false,
            Sources = sources,
            Citations = [],
            ToolsUsed = toolsUsed
        };
    }

    private static string BuildDeterministicAnswer(string question, List<AgentSource> sources)
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
