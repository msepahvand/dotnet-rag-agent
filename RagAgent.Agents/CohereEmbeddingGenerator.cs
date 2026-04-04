using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.AI;

namespace RagAgent.Agents;

/// <summary>
/// Calls the Cohere Embed v3 API on Bedrock directly.
/// The SK Amazon Bedrock connector always uses the Titan inputText request schema regardless of model,
/// so we bypass it and build the correct Cohere request ourselves.
/// </summary>
internal sealed class CohereEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;

    public CohereEmbeddingGenerator(IAmazonBedrockRuntime bedrockRuntime, string modelId)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = modelId;
    }

    public EmbeddingGeneratorMetadata Metadata => new("cohere-bedrock", null, _modelId);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();

        var inputType = options?.AdditionalProperties?.TryGetValue("input_type", out var it) == true
            ? it?.ToString() ?? "search_document"
            : "search_document";

        var body = JsonSerializer.Serialize(new CohereEmbedRequest
        {
            Texts = texts,
            InputType = inputType,
            Truncate = "NONE"
        });

        var response = await _bedrockRuntime.InvokeModelAsync(
            new InvokeModelRequest
            {
                ModelId = _modelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(body))
            },
            cancellationToken);

        using var doc = await JsonDocument.ParseAsync(response.Body, cancellationToken: cancellationToken);

        var result = doc.RootElement
            .GetProperty("embeddings")
            .EnumerateArray()
            .Select(e => new Embedding<float>(e.EnumerateArray().Select(v => v.GetSingle()).ToArray()))
            .ToList();

        return new GeneratedEmbeddings<Embedding<float>>(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private sealed class CohereEmbedRequest
    {
        [JsonPropertyName("texts")]
        public List<string> Texts { get; init; } = [];

        [JsonPropertyName("input_type")]
        public string InputType { get; init; } = "search_document";

        [JsonPropertyName("truncate")]
        public string Truncate { get; init; } = "NONE";
    }
}
