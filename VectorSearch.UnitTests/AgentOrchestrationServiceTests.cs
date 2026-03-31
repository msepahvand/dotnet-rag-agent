using FluentAssertions;
using VectorSearch.Api.Services;
using VectorSearch.Core;

namespace VectorSearch.UnitTests;

public class AgentOrchestrationServiceTests
{
    // ── Grounded passthrough ────────────────────────────────────────────────

    [Fact]
    public async Task AskAsync_WhenResultIsGrounded_ResponseGroundedIsTrue()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "Some answer",
            Grounded = true,
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "S", Distance = 0.1f }],
            Citations = [new Citation { PostId = 1, Quote = "Q" }]
        });
        var sut = new AgentOrchestrationService(stub);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeTrue();
    }

    [Fact]
    public async Task AskAsync_WhenResultIsNotGrounded_ResponseGroundedIsFalse_EvenWithCitationsAndSources()
    {
        // Regression: old code used Citations.Count > 0 || Sources.Count > 0.
        // If the LLM returns grounded:false but still includes citations, the old logic
        // would incorrectly return Grounded=true.
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "Partial answer",
            Grounded = false,
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "S", Distance = 0.1f }],
            Citations = [new Citation { PostId = 1, Quote = "Q" }]
        });
        var sut = new AgentOrchestrationService(stub);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeFalse();
    }

    [Fact]
    public async Task AskAsync_WhenResultIsNotGroundedAndEmpty_ResponseGroundedIsFalse()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "No sources found.",
            Grounded = false,
            Sources = [],
            Citations = []
        });
        var sut = new AgentOrchestrationService(stub);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeFalse();
    }

    // ── Response field mapping ──────────────────────────────────────────────

    [Fact]
    public async Task AskAsync_MapsAllResultFieldsToResponse()
    {
        var source = new AgentSource { PostId = 42, Title = "Title", Snippet = "Snip", Distance = 0.25f };
        var citation = new Citation { PostId = 42, Quote = "Quote text" };
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "The answer",
            Grounded = true,
            Sources = [source],
            Citations = [citation]
        });
        var sut = new AgentOrchestrationService(stub);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Answer.Should().Be("The answer");
        response.ToolUsed.Should().Be("semantic-search");
        response.Sources.Should().ContainSingle().Which.PostId.Should().Be(42);
        response.Citations.Should().ContainSingle().Which.PostId.Should().Be(42);
    }

    // ── TopK normalisation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(100, 10)]
    public async Task AskAsync_NormalizesTopK(int requested, int expected)
    {
        int capturedTopK = 0;
        var stub = new CapturingStubAgentAnswerService(topK => capturedTopK = topK);
        var sut = new AgentOrchestrationService(stub);

        await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = requested });

        capturedTopK.Should().Be(expected);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubAgentAnswerService(AgentAnswerResult result) : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(string question, int topK) =>
            Task.FromResult(result);
    }

    private sealed class CapturingStubAgentAnswerService(Action<int> onAnswer) : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(string question, int topK)
        {
            onAnswer(topK);
            return Task.FromResult(new AgentAnswerResult { Answer = "x", Grounded = false });
        }
    }
}
