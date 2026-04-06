using RagAgent.Core.Models;

namespace RagAgent.Core;

public sealed class PostIndexingService(
    IPostService postService,
    IEmbeddingService embeddingService,
    IVectorService vectorService) : IPostIndexingService
{
    public async Task<IndexAllPostsResult> IndexAllAsync()
    {
        var posts = await postService.GetAllPostsAsync();
        return await IndexPostsAsync(posts);
    }

    public async Task<IndexAllPostsResult> IndexPostsAsync(IReadOnlyList<Post> posts)
    {
        var postList = posts.ToList();
        var postLookup = postList.ToDictionary(p => p.Id);
        var postsWithEmbeddings = new List<(Post Post, float[] Embedding)>();

        await foreach (var (postId, embedding) in embeddingService.StreamEmbeddings(postList))
        {
            if (postLookup.TryGetValue(postId, out var post))
            {
                postsWithEmbeddings.Add((post, embedding));
            }
        }

        await vectorService.IndexPostsBatchAsync(postsWithEmbeddings);
        return new IndexAllPostsResult(postsWithEmbeddings.Count);
    }

    public async Task<IndexSinglePostResult?> IndexSingleAsync(int id)
    {
        var post = await postService.GetPostByIdAsync(id);
        if (post == null)
        {
            return null;
        }

        var embedding = await embeddingService.GenerateEmbeddingAsync($"{post.Title}\n\n{post.Body}");
        await vectorService.IndexPostAsync(post, embedding);

        return new IndexSinglePostResult(post);
    }
}
