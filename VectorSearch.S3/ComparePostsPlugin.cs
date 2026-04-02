using System.ComponentModel;
using Microsoft.SemanticKernel;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class ComparePostsPlugin(IPostService postService)
{
    public const string PluginName = "Compare";

    [KernelFunction("compare_posts")]
    [Description("Retrieves the full content of two posts by their IDs side-by-side. Use this when the user asks to compare, contrast, or find similarities and differences between two specific posts.")]
    public async Task<string> ComparePostsAsync(
        [Description("The integer ID of the first post.")] int postIdA,
        [Description("The integer ID of the second post.")] int postIdB)
    {
        var taskA = postService.GetPostByIdAsync(postIdA);
        var taskB = postService.GetPostByIdAsync(postIdB);
        await Task.WhenAll(taskA, taskB);

        var postA = taskA.Result;
        var postB = taskB.Result;

        var partA = postA == null
            ? $"Post {postIdA}: not found."
            : $"Post {postA.Id} — {postA.Title}\n{postA.Body}";

        var partB = postB == null
            ? $"Post {postIdB}: not found."
            : $"Post {postB.Id} — {postB.Title}\n{postB.Body}";

        return $"=== Post A ===\n{partA}\n\n=== Post B ===\n{partB}";
    }
}
