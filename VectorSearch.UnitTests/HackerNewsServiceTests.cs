using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using VectorSearch.S3;

namespace VectorSearch.UnitTests;

public class HackerNewsServiceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HackerNewsService _sut;

    public HackerNewsServiceTests()
    {
        _server = WireMockServer.Start();
        _sut = Build();
    }

    public void Dispose() => _server.Stop();

    // ── GetAllPostsAsync ─────────────────────────────────────────────────────
    [Fact]
    public async Task GetAllPostsAsync_ReturnsPostForEachValidIdAsync()
    {
        StubTopStories([1, 2]);
        StubItem(1, title: "Post One", text: "Body one.");
        StubItem(2, title: "Post Two", text: "Body two.");

        var posts = await _sut.GetAllPostsAsync();

        posts.Should().HaveCount(2);
        posts.Select(p => p.Title).Should().BeEquivalentTo(["Post One", "Post Two"]);
    }

    [Fact]
    public async Task GetAllPostsAsync_FiltersOutItemsWithNoTitleAsync()
    {
        StubTopStories([1, 2]);
        StubItem(1, title: "Valid Post", text: "Body.");
        StubItem(2, title: null, text: "Body.");

        var posts = await _sut.GetAllPostsAsync();

        posts.Should().ContainSingle(p => p.Title == "Valid Post");
    }

    [Fact]
    public async Task GetAllPostsAsync_FiltersOutItemsThatReturnNullAsync()
    {
        StubTopStories([1, 2]);
        StubItem(1, title: "Valid Post", text: "Body.");
        StubNullItem(2);

        var posts = await _sut.GetAllPostsAsync();

        posts.Should().ContainSingle(p => p.Title == "Valid Post");
    }

    [Fact]
    public async Task GetAllPostsAsync_WhenTopStoriesIsEmpty_ReturnsEmptyListAsync()
    {
        StubTopStories([]);

        var posts = await _sut.GetAllPostsAsync();

        posts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllPostsAsync_RespectsTopStoriesCountConfigAsync()
    {
        StubTopStories([1, 2, 3, 4, 5]);
        foreach (var id in new[] { 1, 2, 3 })
        {
            StubItem(id, title: $"Post {id}", text: "Body.");
        }

        var sut = Build(topStoriesCount: 3);
        var posts = await sut.GetAllPostsAsync();

        posts.Should().HaveCount(3);
    }

    // ── GetPostByIdAsync ─────────────────────────────────────────────────────
    [Fact]
    public async Task GetPostByIdAsync_WhenItemExists_ReturnsPostAsync()
    {
        StubItem(42, title: "My Post", text: "My body text.");

        var post = await _sut.GetPostByIdAsync(42);

        post.Should().NotBeNull();
        post!.Id.Should().Be(42);
        post.Title.Should().Be("My Post");
        post.Body.Should().Be("My body text.");
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenItemHasNoTitle_ReturnsNullAsync()
    {
        StubItem(1, title: null, text: "Body.");

        var post = await _sut.GetPostByIdAsync(1);

        post.Should().BeNull();
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenItemDoesNotExist_ReturnsNullAsync()
    {
        StubNullItem(99);

        var post = await _sut.GetPostByIdAsync(99);

        post.Should().BeNull();
    }

    // ── HTML stripping and body fallbacks ────────────────────────────────────
    [Fact]
    public async Task GetPostByIdAsync_WhenTextContainsHtmlTags_StripsTagsFromBodyAsync()
    {
        StubItem(1, title: "T", text: "<p>Hello <b>world</b>.</p>");

        var post = await _sut.GetPostByIdAsync(1);

        post!.Body.Should().Be("Hello  world .");
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenTextContainsHtmlEntities_DecodesEntitiesAsync()
    {
        StubItem(1, title: "T", text: "Rocks &amp; minerals");

        var post = await _sut.GetPostByIdAsync(1);

        post!.Body.Should().Be("Rocks & minerals");
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenTextIsNullAndUrlExists_UsesUrlAsFallbackBodyAsync()
    {
        StubItem(1, title: "T", text: null, url: "https://example.com/article");

        var post = await _sut.GetPostByIdAsync(1);

        post!.Body.Should().Be("Source URL: https://example.com/article");
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenTextAndUrlAreBothMissing_UsesGenericFallbackBodyAsync()
    {
        StubItem(1, title: "T", text: null, url: null);

        var post = await _sut.GetPostByIdAsync(1);

        post!.Body.Should().Be("No story text was provided for this item.");
    }

    [Fact]
    public async Task GetPostByIdAsync_WhenTextIsWhitespaceOnly_UsesUrlFallbackAsync()
    {
        StubItem(1, title: "T", text: "   ", url: "https://example.com");

        var post = await _sut.GetPostByIdAsync(1);

        post!.Body.Should().Be("Source URL: https://example.com");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private HackerNewsService Build(int? topStoriesCount = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(_server.Url! + "/") };

        var configValues = topStoriesCount.HasValue
            ? new Dictionary<string, string?> { ["DataSource:HackerNews:TopStoriesCount"] = topStoriesCount.ToString() }
            : new Dictionary<string, string?>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new HackerNewsService(client, config);
    }

    private void StubTopStories(IEnumerable<int> ids)
    {
        _server
            .Given(Request.Create().WithPath("/topstories.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(System.Text.Json.JsonSerializer.Serialize(ids)));
    }

    private void StubItem(int id, string? title, string? text, string? url = null)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new { id, title, text, url });
        _server
            .Given(Request.Create().WithPath($"/item/{id}.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    private void StubNullItem(int id)
    {
        _server
            .Given(Request.Create().WithPath($"/item/{id}.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("null"));
    }
}
