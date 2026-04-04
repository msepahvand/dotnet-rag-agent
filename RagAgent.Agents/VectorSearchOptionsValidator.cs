using Microsoft.Extensions.Configuration;

namespace RagAgent.Agents;

public enum VectorStoreProvider
{
    S3Vectors,
    Qdrant,
    Redis
}

public sealed record VectorSearchOptions(
    string EmbeddingModelId,
    string ChatModelId,
    VectorStoreProvider VectorStoreProvider);

public static class VectorSearchOptionsValidator
{
    public static VectorSearchOptions Parse(IConfiguration configuration)
    {
        var embeddingModelId = ParseRequiredModelId(configuration, "AWS:EmbeddingModelId");
        var chatModelId = ParseRequiredModelId(configuration, "AWS:ChatModelId");
        var vectorStoreProvider = ParseProvider(configuration);

        return new VectorSearchOptions(embeddingModelId, chatModelId, vectorStoreProvider);
    }

    public static VectorStoreProvider ParseProvider(IConfiguration configuration)
        => ParseVectorStoreProvider(configuration);

    private static VectorStoreProvider ParseVectorStoreProvider(IConfiguration configuration)
    {
        var configuredProvider = configuration["VectorStore:Provider"];
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            return VectorStoreProvider.S3Vectors;
        }

        if (Enum.TryParse<VectorStoreProvider>(configuredProvider, ignoreCase: true, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"Invalid VectorStore:Provider value '{configuredProvider}'. Valid values are: {string.Join(", ", Enum.GetNames<VectorStoreProvider>())}.");
    }

    private static string ParseRequiredModelId(IConfiguration configuration, string key)
    {
        var configuredValue = configuration[key];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new InvalidOperationException($"Missing required configuration value '{key}'.");
        }

        if (configuredValue.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException($"Invalid configuration value '{key}': model id cannot contain whitespace.");
        }

        return configuredValue;
    }
}
