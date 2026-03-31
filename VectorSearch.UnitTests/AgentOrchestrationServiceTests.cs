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
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

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
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

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
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

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
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

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
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

        await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = requested });

        capturedTopK.Should().Be(expected);
    }

    // ── ConversationId ──────────────────────────────────────────────────────

    [Fact]
    public async Task AskAsync_WithNoConversationId_GeneratesNewConversationId()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.ConversationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AskAsync_WithExistingConversationId_ReturnsTheSameConversationId()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var sut = new AgentOrchestrationService(stub, new InMemoryConversationStore());
        var fixedId = "conv-abc-123";

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5, ConversationId = fixedId });

        response.ConversationId.Should().Be(fixedId);
    }

    [Fact]
    public async Task AskAsync_TwoTurns_SecondTurnReceivesHistory()
    {
        IReadOnlyList<ChatMessage>? capturedHistory = null;
        var stub = new CapturingHistoryStubAgentAnswerService(history => capturedHistory = history);
        var store = new InMemoryConversationStore();
        var sut = new AgentOrchestrationService(stub, store);

        var firstResponse = await sut.AskAsync(new AgentAskRequest { Question = "First question", TopK = 5 });
        await sut.AskAsync(new AgentAskRequest { Question = "Follow-up question", TopK = 5, ConversationId = firstResponse.ConversationId });

        capturedHistory.Should().NotBeNull();
        capturedHistory!.Should().HaveCount(2);
        capturedHistory[0].Role.Should().Be("user");
        capturedHistory[0].Content.Should().Be("First question");
        capturedHistory[1].Role.Should().Be("assistant");
        capturedHistory[1].Content.Should().Be("answer");
    }

    [Fact]
    public async Task AskAsync_StoresUserAndAssistantMessagesAfterEachTurn()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "my answer", Grounded = true });
        var store = new InMemoryConversationStore();
        var sut = new AgentOrchestrationService(stub, store);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Hello?", TopK = 5 });
        var history = await store.GetHistoryAsync(response.ConversationId);

        history.Should().HaveCount(2);
        history[0].Should().Be(new ChatMessage("user", "Hello?"));
        history[1].Should().Be(new ChatMessage("assistant", "my answer"));
    }

    [Fact]
    public async Task AskAsync_TwoSeparateConversations_DoNotShareHistory()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var store = new InMemoryConversationStore();
        var sut = new AgentOrchestrationService(stub, store);

        var r1 = await sut.AskAsync(new AgentAskRequest { Question = "Conv A", TopK = 5 });
        var r2 = await sut.AskAsync(new AgentAskRequest { Question = "Conv B", TopK = 5 });

        r1.ConversationId.Should().NotBe(r2.ConversationId);
        (await store.GetHistoryAsync(r1.ConversationId)).Should().HaveCount(2);
        (await store.GetHistoryAsync(r2.ConversationId)).Should().HaveCount(2);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubAgentAnswerService(AgentAnswerResult result) : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history) =>
            Task.FromResult(result);
    }

    private sealed class CapturingStubAgentAnswerService(Action<int> onAnswer) : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history)
        {
            onAnswer(topK);
            return Task.FromResult(new AgentAnswerResult { Answer = "x", Grounded = false });
        }
    }

    private sealed class CapturingHistoryStubAgentAnswerService(Action<IReadOnlyList<ChatMessage>> onAnswer) : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(string question, int topK, IReadOnlyList<ChatMessage> history)
        {
            onAnswer(history);
            return Task.FromResult(new AgentAnswerResult { Answer = "answer", Grounded = true });
        }
    }
}
