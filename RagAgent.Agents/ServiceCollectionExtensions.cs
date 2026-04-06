using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using RagAgent.Core;
using RagAgent.Agents.Filters;
using RagAgent.Agents.Process;

namespace RagAgent.Agents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVectorSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = VectorSearchOptionsValidator.Parse(configuration);

        // Required by Bedrock chat/embedding connectors regardless of vector store provider.
        services.AddAWSService<IAmazonBedrockRuntime>();

        // Configure Semantic Kernel + embedding pipeline once for all providers.
        // Cohere Embed v3 uses a different request schema to the SK connector's default,
        // so we use a custom generator rather than the SK connector's BedrockEmbeddingGenerator.
        services.AddScoped<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            new CohereEmbeddingGenerator(
                sp.GetRequiredService<IAmazonBedrockRuntime>(),
                options.EmbeddingModelId));
        services.AddBedrockChatCompletionService(options.ChatModelId);

        // Function invocation filters (tool calls): logging/normalisation, then output guardrails.
        services.AddScoped<IFunctionInvocationFilter, ToolInvocationFilter>();
        services.AddScoped<IFunctionInvocationFilter, OutputGuardrailFilter>();

        // Prompt render filter: input guardrails for kernel prompt functions.
        services.AddScoped<IPromptRenderFilter, InputGuardrailFilter>();

        services.AddTransient(sp =>
        {
            var kernel = new Kernel(sp);

            foreach (var filter in sp.GetServices<IFunctionInvocationFilter>())
            {
                kernel.FunctionInvocationFilters.Add(filter);
            }

            foreach (var filter in sp.GetServices<IPromptRenderFilter>())
            {
                kernel.PromptRenderFilters.Add(filter);
            }

            return kernel;
        });
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

        // SK Process orchestration: bridges the process result back to request/response
        services.AddScoped<ProcessResultHolder>();
        services.AddScoped<IAgentAnswerService, ProcessAnswerService>();

        return services;
    }
}
