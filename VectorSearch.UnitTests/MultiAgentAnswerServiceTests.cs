using FluentAssertions;
using VectorSearch.Core.Models;
using VectorSearch.S3;
using VectorSearch.S3.Agents;

namespace VectorSearch.UnitTests;

public class MultiAgentAnswerServiceTests
{
    // ── Iterations ──────────────────────────────────────────────────────────
    [Fact]
    public async Task AnswerAsync_WhenCriticApprovesFirstAnswer_ReturnsIterations1()
    {
        var sut = BuildSut(criticApprovals: [true]);

        var result = await sut.AnswerAsync("Q?", 5, []);

        result.Iterations.Should().Be(1);
    }

    [Fact]
    public async Task AnswerAsync_WhenCriticRejectsOnceThenApproves_ReturnsIterations2()
    {
        var sut = BuildSut(criticApprovals: [false, true]);

        var result = await sut.AnswerAsync("Q?", 5, []);

        result.Iterations.Should().Be(2);
    }

    [Fact]
    public async Task AnswerAsync_WhenCriticAlwaysRejects_CapsAtMaxIterations()
    {
        var sut = BuildSut(criticApprovals: [false, false, false]);

        var result = await sut.AnswerAsync("Q?", 5, []);

        result.Iterations.Should().Be(3);
    }

    // ── Feedback passing ────────────────────────────────────────────────────
    [Fact]
    public async Task AnswerAsync_WhenCriticRejects_PassesFeedbackToNextWriterCall()
    {
        const string criticFeedback = "Citations are missing quotes.";
        string? capturedFeedback = null;

        var writer = new CapturingWriterStub(onCallIndex: 1, feedback => capturedFeedback = feedback);
        var sut = new MultiAgentAnswerService(
            new StubResearcher(),
            writer,
            new SequencedCriticStub([false, true], [criticFeedback, ""]));

        await sut.AnswerAsync("Q?", 5, []);

        capturedFeedback.Should().Be(criticFeedback);
    }

    [Fact]
    public async Task AnswerAsync_OnFirstCall_WriterReceivesNullFeedback()
    {
        string? capturedFeedback = "sentinel";

        var writer = new CapturingWriterStub(onCallIndex: 0, feedback => capturedFeedback = feedback);
        var sut = new MultiAgentAnswerService(
            new StubResearcher(),
            writer,
            new SequencedCriticStub([true], [""]));

        await sut.AnswerAsync("Q?", 5, []);

        capturedFeedback.Should().BeNull();
    }

    // ── Critic not called on final pass ─────────────────────────────────────
    [Fact]
    public async Task AnswerAsync_DoesNotCallCriticAfterFinalIteration()
    {
        var critic = new CountingCriticStub(approves: false);
        var sut = new MultiAgentAnswerService(
            new StubResearcher(),
            new AlwaysPassingWriterStub(),
            critic);

        await sut.AnswerAsync("Q?", 5, []);

        // 3 write passes, but critic is called at most 2 times (not on the 3rd pass)
        critic.CallCount.Should().Be(2);
    }

    // ── TopK normalisation ──────────────────────────────────────────────────
    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(11, 10)]
    [InlineData(5, 5)]
    public async Task AnswerAsync_NormalisesTopKBeforeResearch(int requested, int expected)
    {
        var researcher = new CapturingResearcherStub();
        var sut = new MultiAgentAnswerService(
            researcher,
            new AlwaysPassingWriterStub(),
            new ApprovingCriticStub());

        await sut.AnswerAsync("Q?", requested, []);

        researcher.CapturedTopK.Should().Be(expected);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static MultiAgentAnswerService BuildSut(bool[] criticApprovals)
    {
        var feedbacks = criticApprovals.Select(_ => string.Empty).ToArray();
        return new MultiAgentAnswerService(
            new StubResearcher(),
            new AlwaysPassingWriterStub(),
            new SequencedCriticStub(criticApprovals, feedbacks));
    }

    private static ResearchResult EmptyResearch() => new()
    {
        Sources = [],
        SourcesJson = "[]",
        ToolsUsed = ["search_posts"]
    };

    private static AgentAnswerResult PassingAnswer() => new()
    {
        Answer = "Answer",
        Grounded = true,
        Sources = [],
        Citations = [],
        ToolsUsed = ["search_posts"]
    };

    // ── Stubs ────────────────────────────────────────────────────────────────
    private sealed class StubResearcher : IResearcherAgent
    {
        public Task<ResearchResult> ResearchAsync(string question, int topK) =>
            Task.FromResult(EmptyResearch());
    }

    private sealed class CapturingResearcherStub : IResearcherAgent
    {
        public int CapturedTopK { get; private set; }

        public Task<ResearchResult> ResearchAsync(string question, int topK)
        {
            CapturedTopK = topK;
            return Task.FromResult(EmptyResearch());
        }
    }

    private sealed class AlwaysPassingWriterStub : IWriterAgent
    {
        public Task<AgentAnswerResult> WriteAsync(
            string question, ResearchResult research,
            IReadOnlyList<ChatMessage> history, string? criticFeedback = null) =>
            Task.FromResult(PassingAnswer());
    }

    private sealed class CapturingWriterStub(int onCallIndex, Action<string?> onWrite) : IWriterAgent
    {
        private int _callCount;

        public Task<AgentAnswerResult> WriteAsync(
            string question, ResearchResult research,
            IReadOnlyList<ChatMessage> history, string? criticFeedback = null)
        {
            if (_callCount++ == onCallIndex)
            {
                onWrite(criticFeedback);
            }

            return Task.FromResult(PassingAnswer());
        }
    }

    private sealed class ApprovingCriticStub : ICriticAgent
    {
        public Task<CriticResult> EvaluateAsync(
            string question, AgentAnswerResult answer, ResearchResult research) =>
            Task.FromResult(new CriticResult { Approved = true });
    }

    private sealed class SequencedCriticStub(bool[] approvals, string[] feedbacks) : ICriticAgent
    {
        private int _index;

        public Task<CriticResult> EvaluateAsync(
            string question, AgentAnswerResult answer, ResearchResult research)
        {
            var i = _index < approvals.Length ? _index : approvals.Length - 1;
            _index++;
            return Task.FromResult(new CriticResult
            {
                Approved = approvals[i],
                Feedback = feedbacks[i]
            });
        }
    }

    private sealed class CountingCriticStub(bool approves) : ICriticAgent
    {
        public int CallCount { get; private set; }

        public Task<CriticResult> EvaluateAsync(
            string question, AgentAnswerResult answer, ResearchResult research)
        {
            CallCount++;
            return Task.FromResult(new CriticResult { Approved = approves });
        }
    }
}
