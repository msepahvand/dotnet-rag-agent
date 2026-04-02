using System.ComponentModel;
using Microsoft.SemanticKernel;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class SummarisePlugin(IPostService postService)
{
    public const string PluginName = "Summarise";

    [KernelFunction("summarise_post")]
    [Description("Retrieves the full content of a specific post by its ID. Use this when the user asks to summarise, explain, or get details about a specific post they already know the ID of.")]
    public async Task<string> SummarisePostAsync(
        [Description("The integer ID of the post to retrieve.")] int postId)
    {
        var post = await postService.GetPostByIdAsync(postId);
        if (post == null)
        {
            return $"Post {postId} not found.";
        }

        return $"PostId: {post.Id}\nTitle: {post.Title}\n\n{post.Body}";
    }
}
