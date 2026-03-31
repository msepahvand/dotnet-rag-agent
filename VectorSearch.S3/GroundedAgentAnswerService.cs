using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService(
    Kernel kernel,
    IndexingPlugin indexingPlugin,
    SemanticSearchPlugin semanticSearchPlugin,
    IChatCompletionService? chatCompletionService,
    ILogger<GroundedAgentAnswerService> logger) : IAgentAnswerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static KernelFunction? s_promptFunction;

    public async Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var normalizedTopK = topK <= 0 ? 5 : Math.Min(topK, 10);

        await indexingPlugin.IndexPostsIfEmptyAsync();

        var sourcesJson = await semanticSearchPlugin.SearchPostsAsync(question, normalizedTopK);
        var sources = JsonSerializer.Deserialize<List<AgentSource>>(sourcesJson) ?? [];

        if (sources.Count == 0)
        {
            return new AgentAnswerResult
            {
                Answer = "I couldn't find supporting sources in the indexed content. Try indexing more data or rephrasing the question.",
                Grounded = false,
                Sources = [],
                Citations = []
            };
        }

        var deterministicAnswer = BuildDeterministicAnswer(question, sources);

        if (chatCompletionService == null)
        {
            return new AgentAnswerResult
            {
                Answer = deterministicAnswer,
                Grounded = sources.Count > 0,
                Sources = sources,
                Citations = BuildDeterministicCitations(sources)
            };
        }

        try
        {
            if (history.Count > 0)
            {
                return await AnswerWithHistoryAsync(question, sourcesJson, sources, deterministicAnswer, history);
            }

            var promptFunction = GetPromptFunction();
            var arguments = new KernelArguments
            {
                ["question"] = question,
                ["sources"] = sourcesJson
            };

            var response = await promptFunction.InvokeAsync(kernel, arguments);
            var rawOutput = response.GetValue<string>()?.Trim() ?? "";

            return ParseStructuredAnswer(rawOutput, sources, deterministicAnswer);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic grounded answer because LLM generation failed.");
            return new AgentAnswerResult
            {
                Answer = deterministicAnswer,
                Grounded = sources.Count > 0,
                Sources = sources,
                Citations = BuildDeterministicCitations(sources)
            };
        }
    }

    private async Task<AgentAnswerResult> AnswerWithHistoryAsync(
        string question,
        string sourcesJson,
        List<AgentSource> sources,
        string deterministicAnswer,
        IReadOnlyList<ChatMessage> history)
    {
        var assembly = typeof(GroundedAgentAnswerService).Assembly;
        using var stream = assembly.GetManifestResourceStream("VectorSearch.S3.Prompts.GroundedAnswer.yaml")
            ?? throw new InvalidOperationException("Embedded resource VectorSearch.S3.Prompts.GroundedAnswer.yaml not found.");
        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        // Extract system prompt from YAML template (content between first message block)
        var systemPromptMatch = Regex.Match(yaml, @"\{\{#message role=\""system\""\}\}\s*(.*?)\s*\{\{/message\}\}", RegexOptions.Singleline);
        var systemPrompt = systemPromptMatch.Success
            ? systemPromptMatch.Groups[1].Value.Trim()
            : "You are a grounded assistant that answers questions using ONLY the provided sources. Return your answer as JSON with fields: answer, citations, grounded.";

        var chatHistory = new ChatHistory(systemPrompt);

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

        chatHistory.AddUserMessage($"Sources:\n{sourcesJson}\n\nQuestion: {question}");

        var response = await chatCompletionService!.GetChatMessageContentsAsync(chatHistory, kernel: kernel);
        var rawOutput = response.FirstOrDefault()?.Content?.Trim() ?? "";

        return ParseStructuredAnswer(rawOutput, sources, deterministicAnswer);
    }

    private static AgentAnswerResult ParseStructuredAnswer(
        string rawOutput, List<AgentSource> sources, string fallback)
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
                    Citations = structured.Citations ?? []
                };
            }
        }
        catch (JsonException)
        {
            // LLM returned non-JSON; treat raw text as the answer
        }

        return new AgentAnswerResult
        {
            Answer = string.IsNullOrWhiteSpace(rawOutput) ? fallback : rawOutput,
            Grounded = false,
            Sources = sources,
            Citations = []
        };
    }

    private static string ExtractJson(string raw)
    {
        var fenceMatch = Regex.Match(raw, @"```(?:json)?\s*\n?(.*?)\n?\s*```", RegexOptions.Singleline);
        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            return raw[start..(end + 1)];

        return raw;
    }

    private static string BuildDeterministicAnswer(string question, List<AgentSource> sources)
    {
        var topSources = sources.Take(3).ToList();
        var evidence = string.Join(
            "\n",
            topSources.Select((source, index) =>
                $"{index + 1}. {source.Title} [PostId: {source.PostId}] - {source.Snippet}"));

        return $"Grounded answer for: {question}\n\nSupporting evidence:\n{evidence}";
    }

    private static List<Citation> BuildDeterministicCitations(List<AgentSource> sources)
    {
        return sources.Take(3).Select(s => new Citation
        {
            PostId = s.PostId,
            Quote = s.Snippet.Length > 120 ? s.Snippet[..120] : s.Snippet
        }).ToList();
    }

    private static KernelFunction GetPromptFunction()
    {
        if (s_promptFunction != null) return s_promptFunction;

        var assembly = typeof(GroundedAgentAnswerService).Assembly;
        using var stream = assembly.GetManifestResourceStream("VectorSearch.S3.Prompts.GroundedAnswer.yaml")
            ?? throw new InvalidOperationException("Embedded resource VectorSearch.S3.Prompts.GroundedAnswer.yaml not found.");
        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        s_promptFunction = KernelFunctionYaml.FromPromptYaml(yaml, new HandlebarsPromptTemplateFactory());
        return s_promptFunction;
    }

    private sealed record StructuredLlmAnswer
    {
        [JsonPropertyName("answer")]
        public string Answer { get; init; } = string.Empty;

        [JsonPropertyName("citations")]
        public List<Citation> Citations { get; init; } = [];

        [JsonPropertyName("grounded")]
        public bool Grounded { get; init; }
    }
}
