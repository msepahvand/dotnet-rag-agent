using FluentAssertions;
using Microsoft.Extensions.AI;
using RagAgent.Core.Models;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class WriterAgentTests
{
    // ── JSON parsing ─────────────────────────────────────────────────────────
    [Fact]
    public async Task WriteAsync_WithValidJsonResponse_ParsesAnswerCitationsAndGroundedAsync()
    {
        const string json = """{"answer":"Test answer","citations":[{"postId":1,"quote":"exact quote"}],"grounded":true}""";
        var sut = BuildWriter(json);

        var result = await sut.WriteAsync("Q?", EmptyResearch(), []);

        result.Answer.Should().Be("Test answer");
        result.Grounded.Should().BeTrue();
        result.Citations.Should().ContainSingle(c => c.PostId == 1 && c.Quote == "exact quote");
    }

    [Fact]
    public async Task WriteAsync_WithCodeFencedJsonResponse_ExtractsAndParsesAsync()
    {
        const string fenced = "```json\n{\"answer\":\"Fenced\",\"citations\":[],\"grounded\":false}\n```";
        var sut = BuildWriter(fenced);

        var result = await sut.WriteAsync("Q?", EmptyResearch(), []);

        result.Answer.Should().Be("Fenced");
        result.Grounded.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_WithJsonEmbeddedInProse_ExtractsAndParsesAsync()
    {
        const string prose = """Here is the answer: {"answer":"Embedded","citations":[],"grounded":true} done.""";
        var sut = BuildWriter(prose);

        var result = await sut.WriteAsync("Q?", EmptyResearch(), []);

        result.Answer.Should().Be("Embedded");
        result.Grounded.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_WithUnparseableResponse_FallsBackToRawLlmOutputAsync()
    {
        const string raw = "This is not JSON at all.";
        var sut = BuildWriter(raw);

        var result = await sut.WriteAsync("Q?", EmptyResearch(), []);

        result.Answer.Should().Be(raw);
        result.Grounded.Should().BeFalse();
        result.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_WithEmptyResponse_FallsBackToDeterministicAnswerAsync()
    {
        var research = ResearchWithSources([new AgentSource { PostId = 7, Title = "Some Post", Snippet = "A snippet", Distance = 0.1f }]);
        var sut = BuildWriter(string.Empty);

        var result = await sut.WriteAsync("What is X?", research, []);

        result.Answer.Should().NotBeNullOrWhiteSpace();
        result.Grounded.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_WhenLlmReturnsGroundedFalse_PreservesGroundedFalseAsync()
    {
        const string json = """{"answer":"Insufficient sources","citations":[],"grounded":false}""";
        var sut = BuildWriter(json);

        var result = await sut.WriteAsync("Q?", EmptyResearch(), []);

        result.Grounded.Should().BeFalse();
    }

    // ── Critic feedback injection ─────────────────────────────────────────────
    [Fact]
    public async Task WriteAsync_WithCriticFeedback_InjectsFeedbackMessageBeforeSynthesisInstructionAsync()
    {
        const string json = """{"answer":"Revised","citations":[],"grounded":true}""";
        var chatClient = new ScriptedChatClient(json);
        var sut = new WriterAgent(chatClient);

        await sut.WriteAsync("Q?", EmptyResearch(), [], criticFeedback: "Citations missing quotes.");

        var userMessages = chatClient.LastMessages!
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text)
            .ToList();
        userMessages.Should().Contain(m => m.Contains("Citations missing quotes."));
    }

    [Fact]
    public async Task WriteAsync_WithoutCriticFeedback_DoesNotInjectFeedbackMessageAsync()
    {
        const string json = """{"answer":"A","citations":[],"grounded":true}""";
        var chatClient = new ScriptedChatClient(json);
        var sut = new WriterAgent(chatClient);

        await sut.WriteAsync("Q?", EmptyResearch(), [], criticFeedback: null);

        chatClient.LastMessages.Should().NotContain(m => m.Text.Contains("critic"));
    }

    // ── Conversation history role mapping ────────────────────────────────────
    [Fact]
    public async Task WriteAsync_MapsConversationHistoryRolesToCorrectAuthorRolesAsync()
    {
        const string json = """{"answer":"A","citations":[],"grounded":true}""";
        var chatClient = new ScriptedChatClient(json);
        var sut = new WriterAgent(chatClient);
        var history = new List<ConversationMessage>
        {
            new("user", "First question"),
            new("assistant", "First answer"),
            new("system", "System note")
        };

        await sut.WriteAsync("Q?", EmptyResearch(), history);

        chatClient.LastMessages.Should().Contain(m => m.Role == ChatRole.User && m.Text == "First question");
        chatClient.LastMessages.Should().Contain(m => m.Role == ChatRole.Assistant && m.Text == "First answer");
        chatClient.LastMessages.Should().Contain(m => m.Role == ChatRole.System && m.Text == "System note");
    }

    [Fact]
    public async Task StreamAsync_YieldsTextChunksFromChatClientAsync()
    {
        var chatClient = new ScriptedChatClient(string.Empty, streamedChunks: ["Hello", " world"]);
        var sut = new WriterAgent(chatClient);
        var chunks = new List<string>();

        await foreach (var chunk in sut.StreamAsync("Q?", EmptyResearch(), []))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Equal("Hello", " world");
        chatClient.LastOptions!.MaxOutputTokens.Should().Be(2048);
    }

    // ── ToolsUsed passthrough ────────────────────────────────────────────────
    [Fact]
    public async Task WriteAsync_PreservesToolsUsedFromResearchAsync()
    {
        const string json = """{"answer":"A","citations":[],"grounded":true}""";
        var sut = BuildWriter(json);
        var research = new ResearchResult { SourcesJson = "[]", ToolsUsed = ["search_posts", "extra_tool"] };

        var result = await sut.WriteAsync("Q?", research, []);

        result.ToolsUsed.Should().BeEquivalentTo(["search_posts", "extra_tool"]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static WriterAgent BuildWriter(string llmResponse) =>
        new(new ScriptedChatClient(llmResponse));

    private static ResearchResult EmptyResearch() =>
        new() { Sources = [], SourcesJson = "[]", ToolsUsed = ["search_posts"] };

    private static ResearchResult ResearchWithSources(IReadOnlyList<AgentSource> sources)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(sources);
        return new ResearchResult { Sources = sources, SourcesJson = json, ToolsUsed = ["search_posts"] };
    }
}
