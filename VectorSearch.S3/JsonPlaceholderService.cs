using System.Net.Http.Json;
using VectorSearch.Core;

namespace VectorSearch.S3;

public class JsonPlaceholderService : IPostService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://jsonplaceholder.typicode.com";

    public JsonPlaceholderService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<List<Post>> GetAllPostsAsync()
    {
        var response = await _httpClient.GetAsync("/posts");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Post>>() ?? new List<Post>();
    }

    public async Task<Post?> GetPostByIdAsync(int id)
    {
        var response = await _httpClient.GetAsync($"/posts/{id}");
        if (!response.IsSuccessStatusCode)
            return null;
        
        return await response.Content.ReadFromJsonAsync<Post>();
    }
}
