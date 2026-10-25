using System.Text.Json;
using FluentAssertions;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class CohereEmbeddingGeneratorTests
{
    [Fact]
    public void CreateRequestBody_WhenTextExceedsCohereLimit_TruncatesTo2048Characters()
    {
        var longText = new string('x', 2124);
        longText.Length.Should().BeGreaterThan(2048);
        var body = CohereEmbeddingGenerator.CreateRequestBody([longText], "search_document");
        using var document = JsonDocument.Parse(body);
        var submittedText = document.RootElement.GetProperty("texts")[0].GetString();
        submittedText.Should().HaveLength(2048);
        submittedText.Should().Be(new string('x', 2048));
        document.RootElement.GetProperty("truncate").GetString().Should().Be("END");
    }

    [Fact]
    public void CreateRequestBody_WhenTruncatingAroundSurrogatePair_KeepsValidUtf16()
    {
        var longText = new string('x', 2047) + "\U0001F600" + "y";
        var body = CohereEmbeddingGenerator.CreateRequestBody([longText], "search_document");
        using var document = JsonDocument.Parse(body);
        var submittedText = document.RootElement.GetProperty("texts")[0].GetString();
        submittedText.Should().HaveLength(2047);
        submittedText.Should().Be(new string('x', 2047));
    }
}
