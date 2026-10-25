using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using RagAgent.Core;

namespace RagAgent.HackerNews;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHackerNewsDataSource(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["DataSource:Provider"] ?? "HackerNews";
        if (provider.Equals("Snapshot", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPostService>(serviceProvider =>
            {
                var configuredPath = configuration["DataSource:SnapshotPath"] ?? "eval/corpus.json";
                var path = Path.GetFullPath(configuredPath);

                return new SnapshotPostService(path);
            });
        }
        else if (provider.Equals("HackerNews", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IPostService, HackerNewsService>(client =>
            {
                client.BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/");
            });
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported data source provider '{provider}'. Supported values are HackerNews and Snapshot.");
        }

        return services;
    }
}
