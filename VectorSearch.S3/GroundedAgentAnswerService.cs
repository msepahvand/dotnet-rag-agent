using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class GroundedAgentAnswerService(
    Kernel kernel,
    SemanticSearchPlugin semanticSearchPlugin,
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
            RegisterPluginOnce(kernel, semanticSearchPlugin);

            var initialTopK = Math.Clamp(sources.Count, 1, 10);
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
                ["topK"] = initialTopK
            };

            var response = await kernel.InvokePromptAsync(prompt, arguments);
            var llmAnswer = response.GetValue<string>()?.Trim();

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
