using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.InMemory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryConversationStore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<InMemoryConversationStore>();
        services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemoryConversationStore>());
        services.AddSingleton<IConversationEventStream>(sp => sp.GetRequiredService<InMemoryConversationStore>());
        return services;
    }
}
