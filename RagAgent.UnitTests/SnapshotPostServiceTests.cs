using FluentAssertions;
using RagAgent.Core.Models;
using RagAgent.HackerNews;

namespace RagAgent.UnitTests;

public sealed class SnapshotPostServiceTests
{
    [Fact]
    public async Task GetAllPostsAsync_AndGetPostByIdAsync_ReturnSnapshotPostsAsync()
    {
        var path = CreateSnapshot(
            """[{"id":12,"userId":0,"title":"A story","body":"Story body"}]""");

        try
        {
            var sut = new SnapshotPostService(path);

            var posts = await sut.GetAllPostsAsync();
            var post = await sut.GetPostByIdAsync(12);
            var missing = await sut.GetPostByIdAsync(99);

            posts.Should().ContainSingle().Which.Should().Be(new Post(12, 0, "A story", "Story body"));
            post.Should().Be(posts[0]);
            missing.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Constructor_WhenSnapshotContainsDuplicateIds_ThrowsAsync()
    {
        var path = CreateSnapshot(
            """[{"id":1,"userId":0,"title":"A","body":"A"},{"id":1,"userId":0,"title":"B","body":"B"}]""");

        try
        {
            var act = () => new SnapshotPostService(path);

            act.Should().Throw<InvalidDataException>().WithMessage("*duplicate post IDs*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateSnapshot(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }
}
