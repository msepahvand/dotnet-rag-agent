using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class IndexingStartupService(
    IServiceScopeFactory scopeFactory,
    IngestionTracker tracker,
    ILogger<IndexingStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();
        var postService = scope.ServiceProvider.GetRequiredService<IPostService>();
        var indexingService = scope.ServiceProvider.GetRequiredService<IPostIndexingService>();

        try
        {
            var posts = await postService.GetAllPostsAsync();

            if (!await vectorService.IsIndexEmptyAsync())
            {
                logger.LogInformation(
                    "Vector index already populated — skipping startup indexing. Seeding tracker with {Count} known posts.",
                    posts.Count);
                tracker.MarkIndexed(posts.Select(p => p.Id));
                return;
            }

            logger.LogInformation("Vector index is empty — indexing {Count} posts on startup.", posts.Count);
            var result = await indexingService.IndexPostsAsync(posts);
            tracker.MarkIndexed(posts.Select(p => p.Id));
            logger.LogInformation("Startup indexing complete. Indexed {Count} posts.", result.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup indexing failed. The index may be empty until posts are indexed manually.");
        }
    }
}
