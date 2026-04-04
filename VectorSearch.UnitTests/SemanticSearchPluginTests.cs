using FluentAssertions;
using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3;

namespace VectorSearch.UnitTests;

public class SemanticSearchPluginTests
{
    // ── TrimForSnippet (tested via SearchPostsAsync) ─────────────────────────
    [Fact]
    public async Task SearchPostsAsync_WhenBodyIsWithinLimit_ReturnsBodyAsSnippetAsync()
    {
        const string shortBody = "Short body text.";
        var plugin = BuildPlugin(
            searchResults: [new SearchResult { PostId = 1, Title = "T", Distance = 0.1f }],
            postBody: shortBody);

        var json = await plugin.SearchPostsAsync("q", topK: 1);

        var sources = System.Text.Json.JsonSerializer.Deserialize<List<AgentSource>>(json)!;
        sources.Single().Snippet.Should().Be(shortBody);
    }

    [Fact]
    public async Task SearchPostsAsync_WhenBodyExceedsLimit_TruncatesWithEllipsisAsync()
    {
        var longBody = new string('a', 300);
        var plugin = BuildPlugin(
            searchResults: [new SearchResult { PostId = 1, Title = "T", Distance = 0.1f }],
            postBody: longBody);

        var json = await plugin.SearchPostsAsync("q", topK: 1);

        var sources = System.Text.Json.JsonSerializer.Deserialize<List<AgentSource>>(json)!;
        sources.Single().Snippet.Should().HaveLength(223).And.EndWith("...");
    }

    [Fact]
    public async Task SearchPostsAsync_WhenBodyIsEmpty_ReturnsEmptySnippetAsync()
    {
        var plugin = BuildPlugin(
            searchResults: [new SearchResult { PostId = 1, Title = "T", Distance = 0.1f }],
            postBody: string.Empty);

        var json = await plugin.SearchPostsAsync("q", topK: 1);

        var sources = System.Text.Json.JsonSerializer.Deserialize<List<AgentSource>>(json)!;
        sources.Single().Snippet.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPostsAsync_WhenPostNotFoundForResult_ReturnsEmptySnippetAsync()
    {
        var plugin = BuildPlugin(
            searchResults: [new SearchResult { PostId = 99, Title = "T", Distance = 0.1f }],
            postBody: null);  // null means GetPostByIdAsync returns null

        var json = await plugin.SearchPostsAsync("q", topK: 1);

        var sources = System.Text.Json.JsonSerializer.Deserialize<List<AgentSource>>(json)!;
        sources.Single().Snippet.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPostsAsync_FiltersOutSourcesWithEmptyTitleAsync()
    {
        var plugin = BuildPlugin(
            searchResults:
            [
                new SearchResult { PostId = 1, Title = "Valid title", Distance = 0.1f },
                new SearchResult { PostId = 2, Title = string.Empty, Distance = 0.2f }
            ],
            postBody: "body");

        var json = await plugin.SearchPostsAsync("q", topK: 5);

        var sources = System.Text.Json.JsonSerializer.Deserialize<List<AgentSource>>(json)!;
        sources.Should().ContainSingle(s => s.PostId == 1);
    }

    [Fact]
    public async Task SearchPostsAsync_NormalisesTopKAsync()
    {
        int capturedTopK = 0;
        var vectorService = new CapturingVectorService(topK => capturedTopK = topK);
        var plugin = new SemanticSearchPlugin(vectorService, new StubPostService(body: "b"));

        await plugin.SearchPostsAsync("q", topK: 99);

        capturedTopK.Should().Be(10);  // capped at TopKNormaliser.Max
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static SemanticSearchPlugin BuildPlugin(
        IReadOnlyList<SearchResult> searchResults, string? postBody)
    {
        var vectorService = new StubVectorService(searchResults);
        var postService = new StubPostService(postBody);
        return new SemanticSearchPlugin(vectorService, postService);
    }

    // ── Stubs ────────────────────────────────────────────────────────────────
    private sealed class StubVectorService(IReadOnlyList<SearchResult> results) : IVectorService
    {
        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10) =>
            Task.FromResult(results.ToList());

        public Task IndexPostAsync(Post post, float[] embedding) => Task.CompletedTask;
        public Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts) => Task.CompletedTask;
        public Task<bool> IsIndexEmptyAsync() => Task.FromResult(true);
        public Task EnsureInitializedAsync() => Task.CompletedTask;
    }

    private sealed class CapturingVectorService(Action<int> onSearch) : IVectorService
    {
        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10)
        {
            onSearch(topK);
            return Task.FromResult<List<SearchResult>>([]);
        }

        public Task IndexPostAsync(Post post, float[] embedding) => Task.CompletedTask;
        public Task IndexPostsBatchAsync(List<(Post Post, float[] Embedding)> posts) => Task.CompletedTask;
        public Task<bool> IsIndexEmptyAsync() => Task.FromResult(true);
        public Task EnsureInitializedAsync() => Task.CompletedTask;
    }

    private sealed class StubPostService(string? body) : IPostService
    {
        public Task<Post?> GetPostByIdAsync(int id)
        {
            if (body is null)
            {
                return Task.FromResult<Post?>(null);
            }

            return Task.FromResult<Post?>(new Post(id, UserId: 1, Title: "T", Body: body));
        }

        public Task<List<Post>> GetAllPostsAsync() => Task.FromResult<List<Post>>([]);
    }
}
