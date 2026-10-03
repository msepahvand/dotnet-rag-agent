using Amazon.BedrockAgentCore;
using Amazon.DynamoDBv2;
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

        var lockTableName = configuration["ConversationStore:AgentCore:LockTableName"];
        if (string.IsNullOrWhiteSpace(lockTableName))
        {
            throw new InvalidOperationException(
                "ConversationStore:AgentCore:LockTableName is required when ConversationStore:Provider is AgentCore.");
        }

        services.AddAWSService<IAmazonBedrockAgentCore>();
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddSingleton(new AgentCoreMemoryOptions(memoryId, lockTableName));
        services.AddScoped<IAgentCoreMemoryClient, AgentCoreMemoryClient>();
        services.AddScoped<IDynamoDbConversationLockClient, DynamoDbConversationLockClient>();
        services.AddScoped<IDistributedConversationLock, DynamoDbConversationLockProvider>();
        services.AddScoped<IConversationStore, AgentCoreMemoryConversationStore>();
        services.AddHostedService<AgentCoreMemoryCleanupService>();
        return services;
    }
}
