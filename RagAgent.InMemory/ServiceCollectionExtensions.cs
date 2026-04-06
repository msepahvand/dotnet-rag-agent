using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.InMemory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryConversationStore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        return services;
    }
}
