using System.Text.Json;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.HackerNews;

public sealed class SnapshotPostService : IPostService
{
    private readonly IReadOnlyDictionary<int, Post> _postsById;

    public SnapshotPostService(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        var json = File.ReadAllText(snapshotPath);
        var posts = JsonSerializer.Deserialize<List<Post>>(json)
            ?? throw new InvalidDataException($"The post snapshot '{snapshotPath}' did not contain a JSON array.");

        if (posts.Any(post => post.Id <= 0 || string.IsNullOrWhiteSpace(post.Title)))
        {
            throw new InvalidDataException($"The post snapshot '{snapshotPath}' contains an invalid post.");
        }

        if (posts.Select(post => post.Id).Distinct().Count() != posts.Count)
        {
            throw new InvalidDataException($"The post snapshot '{snapshotPath}' contains duplicate post IDs.");
        }

        _postsById = posts.ToDictionary(post => post.Id);
    }

    public Task<List<Post>> GetAllPostsAsync() =>
        Task.FromResult(_postsById.Values.ToList());

    public Task<Post?> GetPostByIdAsync(int id) =>
        Task.FromResult(_postsById.GetValueOrDefault(id));
}
