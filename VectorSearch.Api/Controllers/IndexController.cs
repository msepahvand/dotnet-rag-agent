using Microsoft.AspNetCore.Mvc;
using VectorSearch.Core;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/index")]
public sealed class IndexController(
    IPostService postService,
    IEmbeddingService embeddingService,
    IVectorService vectorService) : ControllerBase
{
    [HttpPost("all")]
    public async Task<IActionResult> IndexAll()
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

        return Ok(new { Message = $"Indexed {posts.Count} posts successfully", Count = posts.Count });
    }

    [HttpPost("{id:int}")]
    public async Task<IActionResult> IndexSingle(int id)
    {
        var post = await postService.GetPostByIdAsync(id);
        if (post == null)
        {
            return NotFound(new { Message = $"Post {id} not found" });
        }

        var embedding = await embeddingService.GenerateEmbeddingAsync($"{post.Title}\n\n{post.Body}");
        await vectorService.IndexPostAsync(post, embedding);

        return Ok(new { Message = $"Indexed post {id} successfully", Post = post });
    }
}
