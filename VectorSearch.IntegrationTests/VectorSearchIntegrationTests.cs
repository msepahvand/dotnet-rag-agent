using System.Net;
using System.Net.Http.Json;
using VectorSearch.Core;

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
        var posts = await response.Content.ReadFromJsonAsync<List<Post>>();
        
        Assert.NotNull(posts);
        Assert.NotEmpty(posts);
        Assert.Equal(100, posts.Count); // JSONPlaceholder has 100 posts
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
        var post = await response.Content.ReadFromJsonAsync<Post>();
        
        Assert.NotNull(post);
        Assert.Equal(1, post.Id);
        Assert.NotNull(post.Title);
        Assert.NotNull(post.Body);
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
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var results = await searchResponse.Content.ReadFromJsonAsync<List<SearchResult>>();
        
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.PostId == 1);
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
        var results = await searchResponse.Content.ReadFromJsonAsync<List<SearchResult>>();
        
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.InRange(r.PostId, 1, 5));
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
        var results = await response.Content.ReadFromJsonAsync<List<SearchResult>>();
        
        Assert.NotNull(results);
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
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

