using FluentAssertions;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class TextChunkerTests
{
    [Fact]
    public void Split_WhenTextFitsInOneChunk_ReturnsOriginalText()
    {
        const string text = "short document";

        var chunks = TextChunker.Split(text);

        chunks.Should().ContainSingle().Which.Should().Be(text);
    }

    [Fact]
    public void Split_WhenTextExceedsLimit_ReturnsChunksWithinEmbeddingLimit()
    {
        var words = Enumerable.Range(0, 700).Select(index => $"word{index:D3}").ToArray();
        var text = string.Join(' ', words);

        var chunks = TextChunker.Split(text);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(chunk => chunk.Length <= 2048);
        chunks.Select(chunk => chunk.Length).Should().OnlyContain(length => length > 0);
        words.Should().OnlyContain(word => chunks.Any(chunk => chunk.Contains(word, StringComparison.Ordinal)));
    }

    [Fact]
    public void Split_WhenTextContainsSurrogatePair_DoesNotSplitPairAcrossChunks()
    {
        var text = new string('x', 2047) + "\U0001F600" + " word";

        var chunks = TextChunker.Split(text);

        chunks.Should().OnlyContain(chunk =>
            !char.IsHighSurrogate(chunk[chunk.Length - 1])
            && !char.IsLowSurrogate(chunk[0]));
        chunks.Should().Contain(chunk => chunk.Contains("\U0001F600", StringComparison.Ordinal));
    }
}
