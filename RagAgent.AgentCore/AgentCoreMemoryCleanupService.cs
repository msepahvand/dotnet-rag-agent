using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagAgent.Core;

namespace RagAgent.AgentCore;

public sealed class AgentCoreMemoryCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentCoreMemoryCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var conversationStore = scope.ServiceProvider.GetRequiredService<IConversationStore>();
                await conversationStore.ListConversationIdsAsync();
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "AgentCore Memory expired-conversation cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
