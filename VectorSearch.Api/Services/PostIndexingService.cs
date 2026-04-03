using VectorSearch.Api.Services.Contracts;
using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class PostIndexingService(
    IPostService postService,
    IEmbeddingService embeddingService,
    IVectorService vectorService) : IPostIndexingService
{
    public async Task<IndexAllPostsResult> IndexAllAsync()
    {
        var posts = await postService.GetAllPostsAsync();

        var postLookup = posts.ToDictionary(p => p.Id);
        var postsWithEmbeddings = new List<(Core.Models.Post Post, float[] Embedding)>();
        await foreach (var (postId, embedding) in embeddingService.StreamEmbeddings(posts))
        {
            if (postLookup.TryGetValue(postId, out var post))
            {
                postsWithEmbeddings.Add((post, embedding));
            }
        }

        await vectorService.IndexPostsBatchAsync(postsWithEmbeddings);

        return new IndexAllPostsResult(posts.Count);
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
