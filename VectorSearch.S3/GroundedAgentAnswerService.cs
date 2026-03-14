using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService(
    IChatCompletionService? chatCompletionService,
    ILogger<GroundedAgentAnswerService> logger) : IAgentAnswerService
{
    public async Task<string> BuildGroundedAnswerAsync(string question, List<AgentSource> sources)
    {
        if (sources.Count == 0)
        {
            return "I couldn't produce a grounded answer because there were no supporting sources.";
        }

        var deterministicAnswer = BuildDeterministicGroundedAnswer(question, sources);

        if (chatCompletionService == null)
        {
            return deterministicAnswer;
        }

        try
        {
            var sourceContext = string.Join(
                "\n",
                sources.Select(source =>
                    $"[PostId: {source.PostId}] Title: {source.Title}\nSnippet: {source.Snippet}"));

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage("You are a grounded assistant. Answer only from the provided sources. If the sources are insufficient, say you do not have enough information. Include citations using [PostId: N] for each key claim.");
            chatHistory.AddUserMessage($"Question:\n{question}\n\nSources:\n{sourceContext}\n\nProvide a concise answer with citations.");

            var response = await chatCompletionService.GetChatMessageContentsAsync(chatHistory);
            var llmAnswer = response.FirstOrDefault()?.Content?.Trim();

            return string.IsNullOrWhiteSpace(llmAnswer)
                ? deterministicAnswer
                : llmAnswer;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic grounded answer because Bedrock chat generation failed.");
            return deterministicAnswer;
        }
    }

    private static string BuildDeterministicGroundedAnswer(string question, List<AgentSource> sources)
    {
        var topSources = sources.Take(3).ToList();
        var evidence = string.Join(
            "\n",
            topSources.Select((source, index) =>
                $"{index + 1}. {source.Title} [PostId: {source.PostId}] - {source.Snippet}"));

        return $"Grounded answer for: {question}\n\nSupporting evidence:\n{evidence}";
    }
}
