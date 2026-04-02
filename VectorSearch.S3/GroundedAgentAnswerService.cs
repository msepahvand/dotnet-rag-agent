using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService : IAgentAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string SynthesisPrompt =
        "Based solely on the tool results above, respond with a JSON object in this exact shape: " +
        "{\"answer\": \"<your answer>\", \"citations\": [{\"postId\": <N>, \"quote\": \"<excerpt>\"}], \"grounded\": true}. " +
        "Do not use outside knowledge. If the results are insufficient, set grounded to false and explain why in answer.";

    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;

    public GroundedAgentAnswerService(
        Kernel kernel,
        SemanticSearchPlugin semanticSearchPlugin,
        SummarisePlugin summarisePlugin,
        ComparePostsPlugin comparePostsPlugin,
        IChatCompletionService chatCompletionService)
    {
        _kernel = kernel;
        _chatCompletionService = chatCompletionService;

        kernel.ImportPluginFromObject(semanticSearchPlugin, SemanticSearchPlugin.PluginName);
        kernel.ImportPluginFromObject(summarisePlugin, SummarisePlugin.PluginName);
        kernel.ImportPluginFromObject(comparePostsPlugin, ComparePostsPlugin.PluginName);
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
                "system"    => AuthorRole.System,
                _           => AuthorRole.User
            };
            chatHistory.Add(new ChatMessageContent(role, msg.Content));
        }

        chatHistory.AddUserMessage(question);

        // Pass 1 — force at least one tool call; SK auto-invokes and adds results to history
        var retrievalSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Required(autoInvoke: true)
        };
        var pass1 = await _chatCompletionService.GetChatMessageContentsAsync(
            chatHistory, retrievalSettings, _kernel);

        foreach (var msg in pass1)
        {
            chatHistory.Add(msg);
        }

        // Pass 2 — synthesise a structured answer without further tool calls
        chatHistory.AddUserMessage(SynthesisPrompt);
        var synthesisSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.None()
        };
        var pass2 = await _chatCompletionService.GetChatMessageContentsAsync(
            chatHistory, synthesisSettings, _kernel);
        var rawOutput = pass2.FirstOrDefault()?.Content?.Trim() ?? "";

        var toolsUsed = ExtractToolsUsed(chatHistory);
        var sources = ExtractSearchSources(chatHistory);
        var deterministicAnswer = BuildDeterministicAnswer(question, sources);

        return ParseStructuredAnswer(rawOutput, sources, toolsUsed, deterministicAnswer);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<string> ExtractToolsUsed(ChatHistory history)
    {
        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in history)
        {
            if (message.Role != AuthorRole.Assistant)
            {
                continue;
            }

            foreach (var item in message.Items.OfType<FunctionCallContent>())
            {
                tools.Add(item.FunctionName);
            }
        }
        return [.. tools];
    }

    private static List<AgentSource> ExtractSearchSources(ChatHistory history)
    {
        foreach (var message in history)
        {
            if (message.Role != AuthorRole.Tool)
            {
                continue;
            }

            foreach (var item in message.Items.OfType<FunctionResultContent>())
            {
                if (!string.Equals(item.FunctionName, "search_posts", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var json = item.Result?.ToString();
                if (!string.IsNullOrEmpty(json))
                {
                    return JsonSerializer.Deserialize<List<AgentSource>>(json, JsonOptions) ?? [];
                }
            }
        }
        return [];
    }

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
        [JsonPropertyName("answer")]   public string Answer { get; init; } = string.Empty;
        [JsonPropertyName("citations")] public List<Citation> Citations { get; init; } = [];
        [JsonPropertyName("grounded")] public bool Grounded { get; init; }
    }
}
