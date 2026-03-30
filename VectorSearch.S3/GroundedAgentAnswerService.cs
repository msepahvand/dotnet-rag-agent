using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService(
    Kernel kernel,
    IndexingPlugin indexingPlugin,
    SemanticSearchPlugin semanticSearchPlugin,
    IChatCompletionService? chatCompletionService,
    ILogger<GroundedAgentAnswerService> logger) : IAgentAnswerService
{
    public async Task<AgentAnswerResult> AnswerAsync(string question, int topK)
    {
        var normalizedTopK = topK <= 0 ? 5 : Math.Min(topK, 10);

        await indexingPlugin.IndexPostsIfEmptyAsync();

        // Plugin is the single retrieval path — orchestrator no longer calls IVectorService directly.
        var sourcesJson = await semanticSearchPlugin.SearchPostsAsync(question, normalizedTopK);
        var sources = JsonSerializer.Deserialize<List<AgentSource>>(sourcesJson) ?? [];

        if (sources.Count == 0)
        {
            return new AgentAnswerResult
            {
                Answer = "I couldn't find supporting sources in the indexed content. Try indexing more data or rephrasing the question.",
                Sources = []
            };
        }

        var deterministicAnswer = BuildDeterministicGroundedAnswer(question, sources);

        if (chatCompletionService == null)
        {
            return new AgentAnswerResult { Answer = deterministicAnswer, Sources = sources };
        }

        try
        {
            RegisterPluginOnce(kernel, semanticSearchPlugin);

            var prompt =
                "You are a grounded assistant. " +
                "Use the SemanticSearch.search_posts function to retrieve supporting sources before answering. " +
                "Answer only from returned sources. If there is not enough evidence, say so. " +
                "Cite key claims with [PostId: N].\n\n" +
                "Question: {{$question}}\n" +
                "PreferredTopK: {{$topK}}\n" +
                "Provide a concise answer with citations.";

            var arguments = new KernelArguments(
                new PromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                })
            {
                ["question"] = question,
                ["topK"] = normalizedTopK
            };

            var response = await kernel.InvokePromptAsync(prompt, arguments);
            var llmAnswer = response.GetValue<string>()?.Trim();

            return new AgentAnswerResult
            {
                Answer = string.IsNullOrWhiteSpace(llmAnswer) ? deterministicAnswer : llmAnswer,
                Sources = sources
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic grounded answer because Bedrock chat generation failed.");
            return new AgentAnswerResult { Answer = deterministicAnswer, Sources = sources };
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

    private static void RegisterPluginOnce(Kernel kernel, SemanticSearchPlugin semanticSearchPlugin)
    {
        var alreadyRegistered = kernel.Plugins.Any(plugin =>
            string.Equals(plugin.Name, SemanticSearchPlugin.PluginName, StringComparison.Ordinal));

        if (!alreadyRegistered)
        {
            kernel.Plugins.AddFromObject(semanticSearchPlugin, SemanticSearchPlugin.PluginName);
        }
    }
}
