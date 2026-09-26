using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using RagAgent.Core;

namespace RagAgent.Agents.Process;

public static class ProcessServiceCollectionExtensions
{
    public static IServiceCollection AddProcessOrchestration(this IServiceCollection services)
    {
        services.AddTransient(sp => new Kernel(sp));
        services.AddScoped<ProcessResultHolder>();
        services.AddScoped<IAgentAnswerService, ProcessAnswerService>();
        return services;
    }
}
