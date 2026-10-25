using RagAgent.Core;

namespace RagAgent.Api.Services;

/// <summary>
/// Background service that polls for new Hacker News posts on a configurable interval
/// and indexes any posts that have not yet been indexed in this process lifetime.
///
/// The first tick always seeds the tracker rather than indexing (startup indexing via
/// <see cref="IndexingStartupService"/> handles the initial load). Subsequent ticks
/// only index posts whose IDs are absent from the tracker.
///
/// Poll interval is read from <c>Ingestion:PollIntervalMinutes</c> (default: 30).
/// </summary>
public sealed class IngestionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IngestionTracker tracker,
    IConfiguration configuration,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    private TimeSpan PollInterval => TimeSpan.FromMinutes(
        configuration.GetValue<int?>("Ingestion:PollIntervalMinutes") ?? 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give IndexingStartupService a chance to seed the tracker before first poll.
        await Task.Delay(PollInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var postService = scope.ServiceProvider.GetRequiredService<IPostService>();
            var indexingService = scope.ServiceProvider.GetRequiredService<IPostIndexingService>();

            var allPosts = await postService.GetAllPostsAsync();

            if (!tracker.IsSeeded)
            {
                // Startup seeding hasn't happened yet (e.g. startup indexing threw).
                // Seed the tracker from what's currently in top stories so subsequent
                // ticks only pick up genuinely new posts.
                tracker.MarkIndexed(allPosts.Select(p => p.Id));
                logger.LogInformation(
                    "Ingestion agent seeded tracker with {Count} posts (startup seed was missing).",
                    allPosts.Count);
                return;
            }

            var newPosts = allPosts.Where(p => !tracker.IsIndexed(p.Id)).ToList();

            if (newPosts.Count == 0)
            {
                logger.LogDebug("Ingestion agent: no new posts found in top stories.");
                return;
            }

            logger.LogInformation("Ingestion agent: found {Count} new posts — indexing.", newPosts.Count);
            var result = await indexingService.IndexPostsAsync(newPosts);
            tracker.MarkIndexed(newPosts.Select(p => p.Id));
            logger.LogInformation("Ingestion agent: indexed {Count} new posts.", result.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown — no action needed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion agent poll failed.");
        }
    }
}
