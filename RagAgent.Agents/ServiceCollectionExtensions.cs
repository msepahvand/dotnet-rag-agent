using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;
using RagAgent.Agents.Telemetry;
using RagAgent.Agents.Process;

namespace RagAgent.Agents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectorSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = VectorSearchOptionsValidator.Parse(configuration);

        // Required by the Bedrock MEAI chat and Cohere embedding clients.
        services.AddAWSService<IAmazonBedrockRuntime>();

        // Cohere Embed v3 uses a different request schema to the AWS adapter's default,
        // so keep the custom generator for embeddings.
        services.AddScoped<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            new CohereEmbeddingGenerator(
                sp.GetRequiredService<IAmazonBedrockRuntime>(),
                options.EmbeddingModelId));

        services.AddChatClient(sp =>
                sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient(options.ChatModelId))
            .UseLogging()
            .UseOpenTelemetry(
                sourceName: AgentActivitySource.Name,
                configure: client => client.EnableSensitiveData = false)
            .UseFunctionInvocation();

        services.AddScoped<IGuardrailsService, GuardrailsService>();
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<SemanticSearchPlugin>();

        // Register the main vector service
        services.AddScoped<IVectorService, VectorService>();

        // Multi-agent: researcher retrieves, writer synthesises, critic reflects
        services.AddScoped<IResearcherAgent, ResearcherAgent>();
        services.AddScoped<IWriterAgent, WriterAgent>();
        services.AddScoped<ICriticAgent, CriticAgent>();
        services.AddScoped<IEvaluationAgent, EvaluationAgent>();

        // Process orchestration bridges the workflow result back to request/response.
        services.AddProcessOrchestration();

        return services;
    }
}
