using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

public sealed class HackerNewsService(HttpClient httpClient, IConfiguration configuration) : IPostService
{
    private readonly int _topStoriesCount = Math.Clamp(configuration.GetValue<int?>("DataSource:HackerNews:TopStoriesCount") ?? 100, 1, 200);

    public async Task<List<Post>> GetAllPostsAsync()
    {
        var ids = await httpClient.GetFromJsonAsync<List<int>>("topstories.json") ?? [];
        var selectedIds = ids.Take(_topStoriesCount).ToList();

        var storyTasks = selectedIds.Select(GetStoryByIdAsync);
        var stories = await Task.WhenAll(storyTasks);

        return stories
            .Where(post => post != null)
            .Select(post => post!)
            .ToList();
    }

    public async Task<Post?> GetPostByIdAsync(int id)
    {
        return await GetStoryByIdAsync(id);
    }

    private async Task<Post?> GetStoryByIdAsync(int id)
    {
        var item = await httpClient.GetFromJsonAsync<HackerNewsItemDto>($"item/{id}.json");
        if (item == null || string.IsNullOrWhiteSpace(item.Title))
        {
            return null;
        }

        var normalizedText = StripHtml(item.Text);
        var body = string.IsNullOrWhiteSpace(normalizedText)
            ? BuildFallbackBody(item)
            : normalizedText;

        return new Post(
            item.Id,
            0,
            item.Title,
            body);
    }

    private static string BuildFallbackBody(HackerNewsItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            return $"Source URL: {item.Url}";
        }

        return "No story text was provided for this item.";
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = Regex.Replace(html, "<.*?>", " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private sealed record HackerNewsItemDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
