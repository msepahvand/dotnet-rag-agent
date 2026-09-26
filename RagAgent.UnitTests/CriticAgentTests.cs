using FluentAssertions;
using Microsoft.Extensions.AI;
using RagAgent.Core.Models;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class CriticAgentTests
{
    // ── Deterministic citation check ─────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenCitationPostIdNotInSources_ReturnsFailWithoutCallingLlmAsync()
    {
        var chatClient = new ScriptedChatClient(
            string.Empty,
            new InvalidOperationException("LLM should not be called when deterministic check fails."));
        var sut = new CriticAgent(chatClient);
        var answer = AnswerWith(citations: [new Citation { PostId = 99, Quote = "q" }]);
        var research = ResearchWith(sourceIds: [1, 2, 3]);

        var result = await sut.EvaluateAsync("Q?", answer, research);

        result.Approved.Should().BeFalse();
        result.Feedback.Should().Contain("99");
    }

    [Fact]
    public async Task EvaluateAsync_WhenMultipleInvalidPostIds_ListsAllInFeedbackAsync()
    {
        var sut = new CriticAgent(new ScriptedChatClient(
            string.Empty,
            new InvalidOperationException("LLM should not be called when deterministic check fails.")));
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
        var sut = new CriticAgent(new ScriptedChatClient(llmResponse));
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
        var sut = new CriticAgent(new ScriptedChatClient(llmResponse));
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
        var sut = new CriticAgent(new ScriptedChatClient(llmResponse));
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
        var sut = new CriticAgent(new ScriptedChatClient(fenced));
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
        var sut = new CriticAgent(new ScriptedChatClient("I cannot evaluate this."));
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
        var sut = new CriticAgent(new ScriptedChatClient(string.Empty));
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
}
