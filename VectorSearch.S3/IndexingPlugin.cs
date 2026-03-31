using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using VectorSearch.Core;

namespace VectorSearch.S3;

public sealed class IndexingPlugin(
    IPostService postService,
    IEmbeddingService embeddingService,
    IVectorService vectorService,
    ILogger<IndexingPlugin> logger)
{
    public const string PluginName = "Indexing";
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    [KernelFunction("index_posts_if_empty")]
    [Description("Indexes the configured source posts only when the vector index is empty.")]
    public async Task<string> IndexPostsIfEmptyAsync()
    {
        if (!await vectorService.IsIndexEmptyAsync())
        {
            return "Vector index already contains posts.";
        }

        await IndexLock.WaitAsync();
        try
        {
            if (!await vectorService.IsIndexEmptyAsync())
            {
                return "Vector index already contains posts.";
            }

            var posts = await postService.GetAllPostsAsync();
            if (posts.Count == 0)
            {
                return "No posts were available to index.";
            }

            var postLookup = posts.ToDictionary(p => p.Id);
            var postsWithEmbeddings = new List<(Core.Models.Post Post, float[] Embedding)>();
            await foreach (var (postId, embedding) in embeddingService.StreamEmbeddings(posts))
                if (postLookup.TryGetValue(postId, out var post))
                    postsWithEmbeddings.Add((post, embedding));

            await vectorService.IndexPostsBatchAsync(postsWithEmbeddings);
            logger.LogInformation("Indexed {Count} posts because the vector index was empty.", posts.Count);

            return $"Indexed {posts.Count} posts because the vector index was empty.";
        }
        finally
        {
            IndexLock.Release();
        }
    }
}