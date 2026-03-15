using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class AgentOrchestrationService(
    IVectorService vectorService,
    IPostService postService,
    IAgentAnswerService agentAnswerService) : IAgentOrchestrationService
{
    public async Task<AgentAskResponse> AskAsync(AgentAskRequest request)
    {
        var topK = request.TopK <= 0 ? 5 : Math.Min(request.TopK, 10);
        var searchResults = await vectorService.SemanticSearchAsync(request.Question, topK);

        if (searchResults.Count == 0)
        {
            return new AgentAskResponse
            {
                ToolUsed = "semantic-search",
                Grounded = false,
                Answer = "I couldn't find supporting sources in the indexed content. Try indexing more data or rephrasing the question.",
                Sources = []
            };
        }

        var sourceCandidates = await Task.WhenAll(
            searchResults.Select(async result =>
            {
                var post = await postService.GetPostByIdAsync(result.PostId);
                var snippet = post == null
                    ? string.Empty
                    : TrimForSnippet(post.Body, 220);

                return new AgentSource
                {
                    PostId = result.PostId,
                    Title = result.Title,
                    Distance = result.Distance,
                    Snippet = snippet
                };
            }));

        var sources = sourceCandidates
            .Where(source => !string.IsNullOrWhiteSpace(source.Title))
            .ToList();

        var answer = await agentAnswerService.BuildGroundedAnswerAsync(request.Question, sources);

        return new AgentAskResponse
        {
            ToolUsed = "semantic-search",
            Grounded = sources.Count > 0,
            Answer = answer,
            Sources = sources
        };
    }

    private static string TrimForSnippet(string content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (content.Length <= maxLength)
        {
            return content;
        }

        return $"{content[..maxLength].TrimEnd()}...";
    }
}
