using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using RagAgent.Core.Models;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class CriticAgentTests
{
    // ── Deterministic citation check ─────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenCitationPostIdNotInSources_ReturnsFailWithoutCallingLlmAsync()
    {
        var throwingService = new ThrowingChatService();
        var sut = new CriticAgent(throwingService, new Kernel());
        var answer = AnswerWith(citations: [new Citation { PostId = 99, Quote = "q" }]);
        var research = ResearchWith(sourceIds: [1, 2, 3]);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeFalse();
        result.Feedback.Should().Contain("99");
    }

    [Fact]
    public async Task EvaluateAsync_WhenMultipleInvalidPostIds_ListsAllInFeedbackAsync()
    {
        var sut = new CriticAgent(new ThrowingChatService(), new Kernel());
        var answer = AnswerWith(citations:
        [
            new Citation { PostId = 10, Quote = "q" },
            new Citation { PostId = 20, Quote = "q" }
        ]);
        var research = ResearchWith(sourceIds: [1]);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Feedback.Should().Contain("10").And.Contain("20");
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoCitations_ProceedsToLlmEvaluationAsync()
    {
        const string llmResponse = """{"approved":true,"feedback":"","checks":["relevance: PASS"]}""";
        var sut = new CriticAgent(new StubChatService(llmResponse), new Kernel());
        var answer = AnswerWith(citations: []);
        var research = ResearchWith(sourceIds: []);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeTrue();
    }

    // ── LLM evaluation ───────────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenLlmReturnsApproved_ReturnsApprovedAsync()
    {
        const string llmResponse = """{"approved":true,"feedback":"","checks":["relevance: PASS","groundedness: PASS"]}""";
        var sut = new CriticAgent(new StubChatService(llmResponse), new Kernel());
        var answer = AnswerWith(citations: [new Citation { PostId = 1, Quote = "q" }]);
        var research = ResearchWith(sourceIds: [1]);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeTrue();
        result.Checks.Should().Contain("relevance: PASS").And.Contain("groundedness: PASS");
    }

    [Fact]
    public async Task EvaluateAsync_WhenLlmReturnsRejected_ReturnsRejectedWithFeedbackAsync()
    {
        const string llmResponse = """{"approved":false,"feedback":"Answer is not relevant.","checks":["relevance: FAIL","groundedness: PASS"]}""";
        var sut = new CriticAgent(new StubChatService(llmResponse), new Kernel());
        var answer = AnswerWith(citations: [new Citation { PostId = 1, Quote = "q" }]);
        var research = ResearchWith(sourceIds: [1]);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeFalse();
        result.Feedback.Should().Be("Answer is not relevant.");
        result.Checks.Should().Contain("relevance: FAIL");
    }

    [Fact]
    public async Task EvaluateAsync_WhenLlmReturnsCodeFencedJson_ParsesCorrectlyAsync()
    {
        const string fenced = "```json\n{\"approved\":false,\"feedback\":\"Missing citations.\",\"checks\":[]}\n```";
        var sut = new CriticAgent(new StubChatService(fenced), new Kernel());
        var answer = AnswerWith(citations: []);
        var research = ResearchWith(sourceIds: []);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeFalse();
        result.Feedback.Should().Be("Missing citations.");
    }

    // ── Parse-failure fallback ────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenLlmResponseIsUnparseable_FallsBackToApprovedAsync()
    {
        var sut = new CriticAgent(new StubChatService("I cannot evaluate this."), new Kernel());
        var answer = AnswerWith(citations: []);
        var research = ResearchWith(sourceIds: []);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        // Safe default: approve rather than block the pipeline forever
        result.Approved.Should().BeTrue();
        result.Feedback.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WhenLlmResponseIsEmpty_FallsBackToApprovedAsync()
    {
        var sut = new CriticAgent(new StubChatService(string.Empty), new Kernel());
        var answer = AnswerWith(citations: []);
        var research = ResearchWith(sourceIds: []);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static AgentAnswerResult AnswerWith(List<Citation> citations) =>
        new()
        {
            Answer = "An answer",
            Grounded = true,
            Citations = citations,
            Sources = [],
            ToolsUsed = ["search_posts"]
        };

    private static ResearchResult ResearchWith(int[] sourceIds)
    {
        var sources = sourceIds
            .Select(id => new AgentSource { PostId = id, Title = $"Post {id}", Snippet = "s", Distance = 0.1f })
            .ToList();
        return new ResearchResult
        {
            Sources = sources,
            SourcesJson = System.Text.Json.JsonSerializer.Serialize(sources),
            ToolsUsed = ["search_posts"]
        };
    }

    // ── Stubs ────────────────────────────────────────────────────────────────
    private sealed class StubChatService(string responseContent) : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                [new ChatMessageContent(AuthorRole.Assistant, responseContent)]);

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    /// <summary>Stub that throws if called — used to assert the LLM is NOT called.</summary>
    private sealed class ThrowingChatService : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("LLM should not be called when deterministic check fails.");

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
