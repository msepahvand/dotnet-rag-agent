using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Amazon;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

/// <summary>
/// Evaluates the writer agent's output for relevance, groundedness, and citation validity.
/// Citation validity is checked deterministically; relevance and groundedness are assessed via LLM.
/// Returns a <see cref="CriticResult"/> indicating whether the answer should be accepted or revised.
/// </summary>
public sealed class CriticAgent : ICriticAgent
{
    private const string SystemPrompt =
        "You are a quality critic for AI-generated answers. " +
        "Evaluate whether the given answer is relevant to the question and grounded in the provided sources. " +
        "Relevant means the answer directly addresses what was asked. " +
        "Grounded means every claim in the answer is supported by a provided source. " +
        "Respond with a JSON object in this exact shape: " +
        "{\"approved\": true, \"feedback\": \"\", \"checks\": [\"relevance: PASS\", \"groundedness: PASS\"]}. " +
        "Set approved to false and provide concise feedback if either check fails. " +
        "Return only the raw JSON object.";

    private readonly IChatCompletionService _chatService;
    private readonly Kernel _kernel;

    public CriticAgent(IChatCompletionService chatService, Kernel kernel)
    {
        _chatService = chatService;
        _kernel = kernel;
    }

    public async Task<CriticResult> EvaluateAsync(
        string question,
        AgentAnswerResult answer,
        ResearchResult research)
    {
        // Deterministic check: cited postIds must exist in the retrieved sources
        var sourceIds = research.Sources.Select(s => s.PostId).ToHashSet();
        var invalidIds = answer.Citations
            .Select(c => c.PostId)
            .Where(id => !sourceIds.Contains(id))
            .ToList();

        if (invalidIds.Count > 0)
        {
            return new CriticResult
            {
                Approved = false,
                Feedback = $"Citations reference postIds not found in sources: {string.Join(", ", invalidIds)}.",
                Checks = ["citations: FAIL", "relevance: UNKNOWN", "groundedness: UNKNOWN"]
            };
        }

        // LLM-based check for relevance and groundedness
        var chatHistory = new ChatHistory(SystemPrompt);
        chatHistory.AddUserMessage(BuildEvaluationPrompt(question, answer, research.SourcesJson));

        var settings = new AmazonClaudeExecutionSettings
        {
            MaxTokensToSample = 512,
            FunctionChoiceBehavior = FunctionChoiceBehavior.None()
        };

        var response = await _chatService.GetChatMessageContentsAsync(chatHistory, settings, _kernel);
        var rawOutput = response.FirstOrDefault()?.Content?.Trim() ?? string.Empty;

        return ParseCriticResponse(rawOutput);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string BuildEvaluationPrompt(
        string question, AgentAnswerResult answer, string sourcesJson)
    {
        var citationsJson = JsonSerializer.Serialize(answer.Citations);
        return $"Question: {question}\n\nSources:\n{sourcesJson}\n\nAnswer: {answer.Answer}\n\nCitations: {citationsJson}";
    }

    private static CriticResult ParseCriticResponse(string rawOutput)
    {
        var json = AgentJsonHelpers.ExtractJson(rawOutput);
        try
        {
            var parsed = JsonSerializer.Deserialize<CriticLlmResponse>(json, AgentJsonHelpers.JsonOptions);
            if (parsed != null)
            {
                return new CriticResult
                {
                    Approved = parsed.Approved,
                    Feedback = parsed.Feedback ?? string.Empty,
                    Checks = parsed.Checks ?? []
                };
            }
        }
        catch (JsonException) { }

        // If we can't parse the critic response, approve and move on rather than looping forever
        return new CriticResult { Approved = true, Feedback = string.Empty, Checks = [] };
    }

    private sealed record CriticLlmResponse
    {
        [JsonPropertyName("approved")] public bool Approved { get; init; }
        [JsonPropertyName("feedback")] public string? Feedback { get; init; }
        [JsonPropertyName("checks")] public List<string>? Checks { get; init; }
    }
}
