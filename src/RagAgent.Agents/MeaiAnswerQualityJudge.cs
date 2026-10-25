using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Caching.Memory;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

public sealed class MeaiAnswerQualityJudge(IChatClient chatClient, IMemoryCache cache) : IAnswerQualityJudge
{
    private static readonly GroundednessEvaluator GroundednessEvaluator = new();
    private static readonly RelevanceEvaluator RelevanceEvaluator = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    public async Task<AnswerQualityScores> EvaluateAsync(
        string question,
        string answer,
        IReadOnlyList<AgentSource> sources)
    {
        var context = string.Join(
            "\n\n",
            sources.Select(source => $"PostId: {source.PostId}\nTitle: {source.Title}\n{source.Snippet}"));
        var cacheKey = CreateCacheKey(question, answer, context);

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var messages = new[] { new ChatMessage(ChatRole.User, question) };
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
            var chatConfiguration = new ChatConfiguration(chatClient);

            var groundednessResult = await GroundednessEvaluator.EvaluateAsync(
                messages,
                response,
                chatConfiguration,
                [new GroundednessEvaluatorContext(context)]);

            var relevanceResult = await RelevanceEvaluator.EvaluateAsync(
                messages,
                response,
                chatConfiguration);

            return new AnswerQualityScores(
                ReadScore(groundednessResult, GroundednessEvaluator.GroundednessMetricName),
                ReadScore(relevanceResult, RelevanceEvaluator.RelevanceMetricName));
        }) ?? throw new InvalidOperationException("The answer quality judge returned no result.");
    }

    private static double? ReadScore(EvaluationResult result, string metricName)
    {
        if (!result.Metrics.TryGetValue(metricName, out var metric) || metric is not NumericMetric numericMetric)
        {
            throw new InvalidOperationException($"The answer quality judge did not return the '{metricName}' metric.");
        }

        return numericMetric.Value;
    }

    private static string CreateCacheKey(string question, string answer, string context)
    {
        var input = Encoding.UTF8.GetBytes($"{question}\0{answer}\0{context}");
        return $"answer-quality:{Convert.ToHexString(SHA256.HashData(input))}";
    }
}
