using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.Agents;

public static class GuardrailsRegistrationExtensions
{
    public static IServiceCollection AddGuardrails(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Guardrails:Provider"] ?? "Regex";
        var modeName = configuration["Guardrails:Mode"] ?? "Shadow";
        if (!Enum.TryParse<GuardrailsMode>(modeName, ignoreCase: true, out var mode) ||
            !Enum.IsDefined(mode))
        {
            throw new InvalidOperationException("Guardrails:Mode must be Shadow or Enforce.");
        }

        services.AddScoped<GuardrailsService>();
        if (string.Equals(provider, "Regex", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IGuardrailsService>(sp => sp.GetRequiredService<GuardrailsService>());
            return services;
        }

        if (!string.Equals(provider, "Bedrock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Guardrails:Provider must be Regex or Bedrock.");
        }

        var guardrailIdentifier = configuration["Guardrails:Bedrock:GuardrailIdentifier"];
        var guardrailVersion = configuration["Guardrails:Bedrock:GuardrailVersion"];
        if (string.IsNullOrWhiteSpace(guardrailIdentifier) || string.IsNullOrWhiteSpace(guardrailVersion))
        {
            throw new InvalidOperationException(
                "Guardrails:Bedrock:GuardrailIdentifier and Guardrails:Bedrock:GuardrailVersion are required when the Bedrock provider is selected.");
        }

        services.AddScoped<IBedrockGuardrailClient, BedrockGuardrailClient>();
        services.AddScoped(sp => new BedrockGuardrailsOptions(guardrailIdentifier, guardrailVersion, mode));
        services.AddScoped<IGuardrailsService, BedrockGuardrailsService>();
        return services;
    }
}
