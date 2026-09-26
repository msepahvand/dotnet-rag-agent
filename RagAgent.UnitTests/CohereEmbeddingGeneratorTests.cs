using System.Text.Json;
using FluentAssertions;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class CohereEmbeddingGeneratorTests
{
    [Fact]
    public void CreateRequestBody_WhenTextExceedsCohereLimit_RequestsEndTruncation()
    {
        var longText = new string('x', 2124);
        longText.Length.Should().BeGreaterThan(2048);
        var body = CohereEmbeddingGenerator.CreateRequestBody([longText], "search_document");
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("texts")[0].GetString().Should().Be(longText);
        document.RootElement.GetProperty("truncate").GetString().Should().Be("END");
    }
}
