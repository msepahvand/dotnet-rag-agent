using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.IntegrationTests;

internal sealed class TestPostService : IPostService
{
    private static readonly List<Post> Posts = Enumerable
        .Range(1, 100)
        .Select(id => new Post(
            id,
            (id - 1) % 10 + 1,
            id == 1 ? "sunt aut facere repellat provident occaecati excepturi optio reprehenderit" : $"Test post title {id}",
            id == 1
                ? "This is deterministic content for post 1 and includes sunt aut facere so search assertions remain stable."
                : id == 2
                    ? string.Join(' ', Enumerable.Range(0, 700).Select(index => $"long-post-token-{index:D3}"))
                : $"Deterministic integration-test content for post {id}."))
        .ToList();

    public Task<List<Post>> GetAllPostsAsync()
    {
        return Task.FromResult(Posts);
    }

    public Task<Post?> GetPostByIdAsync(int id)
    {
        var post = Posts.FirstOrDefault(p => p.Id == id);
        return Task.FromResult(post);
    }
}
