using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VectorSearch.Api.Services.Contracts;
using VectorSearch.Core;

namespace VectorSearch.Api.Services;

public sealed class IndexingStartupService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<IndexingStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();
        var indexingService = scope.ServiceProvider.GetRequiredService<IPostIndexingService>();

        try
        {
            if (!await vectorService.IsIndexEmptyAsync())
            {
                logger.LogInformation("Vector index already populated — skipping startup indexing.");
                return;
            }

            logger.LogInformation("Vector index is empty — indexing posts on startup.");
            var result = await indexingService.IndexAllAsync();
            logger.LogInformation("Startup indexing complete. Indexed {Count} posts.", result.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup indexing failed. The index may be empty until posts are indexed manually.");
        }
    }
}
