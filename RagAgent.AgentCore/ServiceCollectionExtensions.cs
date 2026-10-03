using Amazon.BedrockAgentCore;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.AgentCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentCoreMemoryConversationStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var memoryId = configuration["ConversationStore:AgentCore:MemoryId"];
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            throw new InvalidOperationException(
                "ConversationStore:AgentCore:MemoryId is required when ConversationStore:Provider is AgentCore.");
        }

        services.AddAWSService<IAmazonBedrockAgentCore>();
        services.AddSingleton(new AgentCoreMemoryOptions(memoryId));
        services.AddScoped<IAgentCoreMemoryClient, AgentCoreMemoryClient>();
        services.AddScoped<IConversationStore, AgentCoreMemoryConversationStore>();
        return services;
    }
}
