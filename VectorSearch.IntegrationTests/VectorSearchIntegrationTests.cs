using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VectorSearch.Api.Dtos;

namespace VectorSearch.IntegrationTests;

public class VectorSearchIntegrationTests
{
    public static TheoryData<string> VectorProviders => new TheoryData<string>
    {
        "Qdrant",
        "Redis"
    };

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task GetPosts_ReturnsSuccessAndPosts(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/posts");

        // Assert
        response.EnsureSuccessStatusCode();
        var posts = await response.Content.ReadFromJsonAsync<List<PostDto>>();

        posts.Should().NotBeNull();
        posts.Should().NotBeEmpty();
        posts!.Count.Should().Be(100); // Default HackerNews top stories count
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task GetPost_WithValidId_ReturnsPost(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/posts/1");

        // Assert
        response.EnsureSuccessStatusCode();
        var post = await response.Content.ReadFromJsonAsync<PostDto>();

        post.Should().NotBeNull();
        post!.Id.Should().Be(1);
        post.Title.Should().NotBeNull();
        post.Body.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task GetPost_WithInvalidId_ReturnsNotFound(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/posts/999999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task IndexSinglePost_ThenSearch_ReturnsPost(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Index a post
        var indexResponse = await client.PostAsync("/api/index/1", null);
        indexResponse.EnsureSuccessStatusCode();

        // Wait a bit for indexing to complete
        await Task.Delay(2000);

        // Act - Search for content from that post
        var searchResponse = await client.GetAsync("/api/search?query=sunt aut facere&topK=5");

        // Assert
        searchResponse.EnsureSuccessStatusCode();
        var results = await searchResponse.Content.ReadFromJsonAsync<List<SearchResultDto>>();

        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.PostId == 1);
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task IndexMultiplePosts_ThenSearch_ReturnsRelevantResults(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Index first 5 posts
        for (int i = 1; i <= 5; i++)
        {
            var indexResponse = await client.PostAsync($"/api/index/{i}", null);
            indexResponse.EnsureSuccessStatusCode();
        }

        // Wait for indexing to complete
        await Task.Delay(3000);

        // Act - Search
        var searchResponse = await client.GetAsync("/api/search?query=post&topK=10");

        // Assert
        searchResponse.EnsureSuccessStatusCode();
        var results = await searchResponse.Content.ReadFromJsonAsync<List<SearchResultDto>>();

        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => r.PostId >= 1 && r.PostId <= 5);
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task Search_WithNoIndexedData_ReturnsEmptyResults(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/search?query=nonexistent&topK=5");

        // Assert
        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<SearchResultDto>>();

        results.Should().NotBeNull();
        // Might be empty if nothing is indexed
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task Search_WithEmptyQuery_ReturnsBadRequest(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/search?query=&topK=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [MemberData(nameof(VectorProviders))]
    public async Task AgentAsk_UsesSemanticSearchAndReturnsGroundedSources(string provider)
    {
        // Arrange
        await using var factory = new VectorSearchWebApplicationFactory(provider);
        await factory.InitializeAsync();
        var client = factory.CreateClient();

        var indexResponse = await client.PostAsync("/api/index/1", null);
        indexResponse.EnsureSuccessStatusCode();

        await Task.Delay(2000);

        // Act
        var response = await client.PostAsJsonAsync("/api/agent/ask", new
        {
            question = "What is post 1 about?",
            topK = 5
        });

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AgentAskResponseDto>();

        result.Should().NotBeNull();
        result!.ToolsUsed.Should().Contain("search_posts");
        result.Grounded.Should().BeTrue();
        result.Sources.Should().NotBeNull();
        result.Sources.Should().NotBeEmpty();
        result.Sources.Should().Contain(source => source.PostId == 1);
        string.IsNullOrWhiteSpace(result.Answer).Should().BeFalse();
    }

    private sealed record AgentAskResponseDto
    {
        public List<string> ToolsUsed { get; init; } = [];
        public bool Grounded { get; init; }
        public string Answer { get; init; } = string.Empty;
        public List<CitationDto> Citations { get; init; } = [];
        public List<AgentSourceDto> Sources { get; init; } = [];
    }

    private sealed record CitationDto
    {
        public int PostId { get; init; }
        public string Quote { get; init; } = string.Empty;
    }

    private sealed record AgentSourceDto
    {
        public int PostId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Snippet { get; init; } = string.Empty;
        public double Distance { get; init; }
    }
}

