using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.HackerNews;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHackerNewsDataSource(this IServiceCollection services)
    {
        services.AddHttpClient<IPostService, HackerNewsService>(client =>
        {
            client.BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/");
        });

        return services;
    }
}
