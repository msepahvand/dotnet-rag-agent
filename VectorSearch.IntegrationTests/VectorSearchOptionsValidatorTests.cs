using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VectorSearch.S3;

namespace VectorSearch.IntegrationTests;

public class VectorSearchOptionsValidatorTests
{
    [Fact]
    public void Parse_WithValidValues_ReturnsTypedOptions()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AWS:EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
            ["AWS:ChatModelId"] = "anthropic.claude-3-7-sonnet-20250219-v1:0",
            ["VectorStore:Provider"] = "qdrant"
        });

        var result = VectorSearchOptionsValidator.Parse(configuration);

        result.EmbeddingModelId.Should().Be("amazon.titan-embed-text-v2:0");
        result.ChatModelId.Should().Be("anthropic.claude-3-7-sonnet-20250219-v1:0");
        result.VectorStoreProvider.Should().Be(VectorStoreProvider.Qdrant);
    }

    [Fact]
    public void Parse_WithMissingEmbeddingModel_ThrowsClearError()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AWS:ChatModelId"] = "anthropic.claude-3-7-sonnet-20250219-v1:0"
        });

        Action action = () => VectorSearchOptionsValidator.Parse(configuration);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AWS:EmbeddingModelId*");
    }

    [Fact]
    public void Parse_WithWhitespaceInChatModel_ThrowsClearError()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AWS:EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
            ["AWS:ChatModelId"] = "anthropic claude"
        });

        Action action = () => VectorSearchOptionsValidator.Parse(configuration);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AWS:ChatModelId*")
            .WithMessage("*cannot contain whitespace*");
    }

    [Fact]
    public void Parse_WithInvalidProvider_ThrowsAndListsValidValues()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AWS:EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
            ["AWS:ChatModelId"] = "anthropic.claude-3-7-sonnet-20250219-v1:0",
            ["VectorStore:Provider"] = "pinecone"
        });

        Action action = () => VectorSearchOptionsValidator.Parse(configuration);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*VectorStore:Provider*")
            .WithMessage("*S3Vectors*")
            .WithMessage("*Qdrant*")
            .WithMessage("*Redis*");
    }

    [Fact]
    public void Parse_WithMissingProvider_DefaultsToS3Vectors()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AWS:EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
            ["AWS:ChatModelId"] = "anthropic.claude-3-7-sonnet-20250219-v1:0"
        });

        var result = VectorSearchOptionsValidator.Parse(configuration);

        result.VectorStoreProvider.Should().Be(VectorStoreProvider.S3Vectors);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
