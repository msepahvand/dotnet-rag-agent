using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.IntegrationTests;

internal sealed class TestAgentAnswerService(
    IVectorService vectorService,
    IPostService postService) : IAgentAnswerService
{
    public async Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history)
    {
        var searchResults = await vectorService.SemanticSearchAsync(question, topK);

        if (searchResults.Count == 0)
        {
            return new AgentAnswerResult
            {
                Answer = "I couldn't find enough grounded sources.",
                Grounded = false,
                Sources = []
            };
        }

        var sourceCandidates = await Task.WhenAll(
            searchResults.Select(async result =>
            {
                var post = await postService.GetPostByIdAsync(result.PostId);
                var snippet = post == null
                    ? string.Empty
                    : post.Body.Length > 220 ? post.Body[..220].TrimEnd() + "..." : post.Body;

                return new AgentSource
                {
                    PostId = result.PostId,
                    Title = result.Title,
                    Distance = result.Distance,
                    Snippet = snippet
                };
            }));

        var sources = sourceCandidates
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .ToList();

        var citationList = sources.Take(3).Select(s => new Citation
        {
            PostId = s.PostId,
            Quote = s.Snippet.Length > 80 ? s.Snippet[..80] : s.Snippet
        }).ToList();

        var citationRefs = string.Join(", ", citationList.Select(c => $"[PostId: {c.PostId}]"));

        return new AgentAnswerResult
        {
            Answer = $"Grounded test answer for '{question}' using {citationRefs}.",
            Grounded = sources.Count > 0,
            Sources = sources,
            Citations = citationList,
            ToolsUsed = ["search_posts"]
        };
    }
}
