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
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(posts);

        var postsWithEmbeddings = posts
            .Join(embeddings,
                post => post.Id,
                embedding => embedding.PostId,
                (post, embedding) => (Post: post, Embedding: embedding.Embedding))
            .ToList();

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
