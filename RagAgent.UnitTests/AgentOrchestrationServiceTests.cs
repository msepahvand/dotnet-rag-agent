using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using RagAgent.Api.Services;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public class AgentOrchestrationServiceTests
{
    // ── Grounded passthrough ────────────────────────────────────────────────
    [Fact]
    public async Task AskAsync_WhenResultIsGrounded_ResponseGroundedIsTrueAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "Some answer",
            Grounded = true,
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "S", Distance = 0.1f }],
            Citations = [new Citation { PostId = 1, Quote = "Q" }]
        });
        var sut = new AgentOrchestrationService(stub, CreateStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeTrue();
    }

    [Fact]
    public async Task AskAsync_WhenResultIsNotGrounded_ResponseGroundedIsFalse_EvenWithCitationsAndSourcesAsync()
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
        var sut = new AgentOrchestrationService(stub, CreateStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeFalse();
    }

    [Fact]
    public async Task AskAsync_WhenResultIsNotGroundedAndEmpty_ResponseGroundedIsFalseAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult
        {
            Answer = "No sources found.",
            Grounded = false,
            Sources = [],
            Citations = []
        });
        var sut = new AgentOrchestrationService(stub, CreateStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Grounded.Should().BeFalse();
    }

    // ── Response field mapping ──────────────────────────────────────────────
    [Fact]
    public async Task AskAsync_MapsAllResultFieldsToResponseAsync()
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
        var sut = new AgentOrchestrationService(stub, CreateStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.Answer.Should().Be("The answer");
        response.ToolsUsed.Should().Contain("search_posts");
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
    public async Task AskAsync_NormalisesTopKAsync(int requested, int expected)
    {
        int capturedTopK = 0;
        var stub = new CapturingStubAgentAnswerService(topK => capturedTopK = topK);
        var sut = new AgentOrchestrationService(stub, CreateStore());

        await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = requested });

        capturedTopK.Should().Be(expected);
    }

    // ── ConversationId ──────────────────────────────────────────────────────
    [Fact]
    public async Task AskAsync_WithNoConversationId_GeneratesNewConversationIdAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var sut = new AgentOrchestrationService(stub, CreateStore());

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5 });

        response.ConversationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AskAsync_WithExistingConversationId_ReturnsTheSameConversationIdAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var sut = new AgentOrchestrationService(stub, CreateStore());
        var fixedId = "conv-abc-123";

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Q?", TopK = 5, ConversationId = fixedId });

        response.ConversationId.Should().Be(fixedId);
    }

    [Fact]
    public async Task AskAsync_TwoTurns_SecondTurnReceivesHistoryAsync()
    {
        IReadOnlyList<ChatMessage>? capturedHistory = null;
        var stub = new CapturingHistoryStubAgentAnswerService(history => capturedHistory = history);
        var store = CreateStore();
        var sut = new AgentOrchestrationService(stub, store);

        var firstResponse = await sut.AskAsync(new AgentAskRequest { Question = "First question", TopK = 5 });
        await sut.AskAsync(new AgentAskRequest { Question = "Follow-up question", TopK = 5, ConversationId = firstResponse.ConversationId });

        capturedHistory.Should().NotBeNull();
        capturedHistory!.Should().HaveCount(2);
        capturedHistory[0]!.Role.Should().Be("user");
        capturedHistory[0]!.Content.Should().Be("First question");
        capturedHistory[1]!.Role.Should().Be("assistant");
        capturedHistory[1]!.Content.Should().Be("answer");
    }

    [Fact]
    public async Task AskAsync_StoresUserAndAssistantMessagesAfterEachTurnAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "my answer", Grounded = true });
        var store = CreateStore();
        var sut = new AgentOrchestrationService(stub, store);

        var response = await sut.AskAsync(new AgentAskRequest { Question = "Hello?", TopK = 5 });
        var history = await store.GetHistoryAsync(response.ConversationId);

        history.Should().HaveCount(2);
        history[0].Should().Be(new ChatMessage("user", "Hello?"));
        history[1].Should().Be(new ChatMessage("assistant", "my answer"));
    }

    [Fact]
    public async Task AskAsync_TwoSeparateConversations_DoNotShareHistoryAsync()
    {
        var stub = new StubAgentAnswerService(new AgentAnswerResult { Answer = "ok", Grounded = true });
        var store = CreateStore();
        var sut = new AgentOrchestrationService(stub, store);

        var r1 = await sut.AskAsync(new AgentAskRequest { Question = "Conv A", TopK = 5 });
        var r2 = await sut.AskAsync(new AgentAskRequest { Question = "Conv B", TopK = 5 });

        r1.ConversationId.Should().NotBe(r2.ConversationId);
        (await store.GetHistoryAsync(r1.ConversationId)).Should().HaveCount(2);
        (await store.GetHistoryAsync(r2.ConversationId)).Should().HaveCount(2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static InMemoryConversationStore CreateStore() =>
        new(new MemoryCache(new MemoryCacheOptions()));

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
