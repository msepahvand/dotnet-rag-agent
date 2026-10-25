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
        var postEmbeddings = new List<PostEmbedding>();

        await foreach (var postEmbedding in embeddingService.StreamEmbeddings(postList))
        {
            postEmbeddings.Add(postEmbedding);
        }

        await vectorService.IndexPostsBatchAsync(postEmbeddings);
        return new IndexAllPostsResult(postEmbeddings.Select(embedding => embedding.Post.Id).Distinct().Count());
    }

    public async Task<IndexSinglePostResult?> IndexSingleAsync(int id)
    {
        var post = await postService.GetPostByIdAsync(id);
        if (post == null)
        {
            return null;
        }

        var embeddings = await embeddingService.GenerateEmbeddingsAsync($"{post.Title}\n\n{post.Body}");
        await vectorService.IndexPostAsync(post, embeddings);

        return new IndexSinglePostResult(post);
    }
}
