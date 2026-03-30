using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class SemanticSearchPlugin(IVectorService vectorService, IPostService postService)
{
    public const string PluginName = "SemanticSearch";

    [KernelFunction("search_posts")]
    [Description("Runs semantic search over indexed posts and returns grounded sources with PostId, title, snippet, and distance.")]
    public async Task<string> SearchPostsAsync(
        [Description("Natural language question to search for.")] string question,
        [Description("Maximum number of results to return, between 1 and 10.")] int topK = 5)
    {
        var normalizedTopK = topK <= 0 ? 5 : Math.Min(topK, 10);
        var searchResults = await vectorService.SemanticSearchAsync(question, normalizedTopK);

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

        return JsonSerializer.Serialize(sources);
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
