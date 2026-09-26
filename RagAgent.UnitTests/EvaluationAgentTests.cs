using FluentAssertions;
using RagAgent.Core;
using RagAgent.Core.Models;
using RagAgent.Agents;

namespace RagAgent.UnitTests;

public class EvaluationAgentTests
{
    [Fact]
    public async Task EvaluateAsync_CalculatesP50AndP95LatencyAsync()
    {
        var timer = new AdvancingTimeProvider(
            TimeSpan.Zero.Ticks,
            TimeSpan.FromMilliseconds(10).Ticks,
            TimeSpan.FromMilliseconds(10).Ticks,
            TimeSpan.FromMilliseconds(30).Ticks,
            TimeSpan.FromMilliseconds(30).Ticks,
            TimeSpan.FromMilliseconds(60).Ticks);
        var sut = new EvaluationAgent(new StubAnswerService(new AgentAnswerResult()), timeProvider: timer);

        var report = await sut.EvaluateAsync([new("Q1", []), new("Q2", []), new("Q3", [])]);

        report.Results.Select(result => result.LatencyMs).Should().Equal(10, 20, 30);
        report.P50LatencyMs.Should().Be(20);
        report.P95LatencyMs.Should().Be(29);
    }

    [Fact]
    public async Task EvaluateAsync_UsesIndependentJudgeScoresAsync()
    {
        var judge = new StubAnswerQualityJudge(new AnswerQualityScores(4.5, 3.5));
        var sut = new EvaluationAgent(new StubAnswerService(new AgentAnswerResult { Answer = "Answer" }), judge);

        var report = await sut.EvaluateAsync([new("Question", [])]);

        report.Results.Single().JudgedGroundednessScore.Should().Be(4.5);
        report.Results.Single().JudgedRelevanceScore.Should().Be(3.5);
        report.AverageJudgedGroundednessScore.Should().Be(4.5);
        report.AverageJudgedRelevanceScore.Should().Be(3.5);
        judge.CallCount.Should().Be(1);
    }

    // ── Hit@k ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenExpectedPostIdInSources_RecordsHitAtKTrueAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources = [new AgentSource { PostId = 42, Title = "T", Snippet = "s", Distance = 0.1f }],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [42])]);

        report.Results.Single().HitAtK.Should().BeTrue();
        report.HitAtKRate.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenExpectedPostIdNotInSources_RecordsHitAtKFalseAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "s", Distance = 0.1f }],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [99])]);

        report.Results.Single().HitAtK.Should().BeFalse();
        report.HitAtKRate.Should().Be(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoExpectedPostIds_HitAtKIsNullAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Grounded = false });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().HitAtK.Should().BeNull();
        report.HitAtKRate.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenAnyExpectedIdMatches_RecordsHitAtKTrueAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources = [new AgentSource { PostId = 5, Title = "T", Snippet = "s", Distance = 0.1f }],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [1, 2, 5])]);

        report.Results.Single().HitAtK.Should().BeTrue();
    }

    // ── Groundedness ─────────────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenAnswerIsGrounded_RecordsGroundedTrueAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Grounded = true });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().Grounded.Should().BeTrue();
        report.GroundednessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenAnswerIsNotGrounded_RecordsGroundedFalseAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Grounded = false });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().Grounded.Should().BeFalse();
        report.GroundednessRate.Should().Be(0.0);
    }

    // ── Citation validity ─────────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_WhenAllCitationsInSources_RecordsCitationsValidAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "s", Distance = 0.1f }],
            Citations = [new Citation { PostId = 1, Quote = "q" }],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().CitationsValid.Should().BeTrue();
        report.CitationValidityRate.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCitationPostIdNotInSources_RecordsCitationsInvalidAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "s", Distance = 0.1f }],
            Citations = [new Citation { PostId = 99, Quote = "q" }],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().CitationsValid.Should().BeFalse();
        report.CitationValidityRate.Should().Be(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoCitations_RecordsCitationsValidAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Citations = [], Grounded = false });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        report.Results.Single().CitationsValid.Should().BeTrue();
    }

    // ── Aggregation ──────────────────────────────────────────────────────────
    [Fact]
    public async Task EvaluateAsync_AggregatesGroundednessRateAcrossQuestionsAsync()
    {
        var answers = new Queue<AgentAnswerResult>([
            new() { Sources = [], Grounded = true },
            new() { Sources = [], Grounded = false },
            new() { Sources = [], Grounded = true },
        ]);
        var sut = Build(answers);

        var report = await sut.EvaluateAsync(
        [
            new("Q1", []),
            new("Q2", []),
            new("Q3", []),
        ]);

        report.GroundednessRate.Should().BeApproximately(2.0 / 3.0, 0.001);
        report.TotalQuestions.Should().Be(3);
    }

    [Fact]
    public async Task EvaluateAsync_AggregatesHitAtKRateOnlyForQuestionsWithExpectedIdsAsync()
    {
        var answers = new Queue<AgentAnswerResult>([
            new() { Sources = [new AgentSource { PostId = 1, Title = "T", Snippet = "s", Distance = 0.1f }], Grounded = true },
            new() { Sources = [new AgentSource { PostId = 2, Title = "T", Snippet = "s", Distance = 0.1f }], Grounded = true },
            new() { Sources = [], Grounded = false },  // no expected IDs — excluded from Hit@k
        ]);
        var sut = Build(answers);

        var report = await sut.EvaluateAsync(
        [
            new("Q1", [1]),   // hit
            new("Q2", [99]),  // miss
            new("Q3", []),    // no expected
        ]);

        report.HitAtKRate.Should().BeApproximately(0.5, 0.001);  // 1 of 2 questions with expected IDs
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoQuestionsHaveExpectedIds_HitAtKRateIsNullAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Grounded = false });

        var report = await sut.EvaluateAsync([new("Q?", []), new("Q2?", [])]);

        report.HitAtKRate.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_RecordsRetrievedCountAndPostIdsAsync()
    {
        var sut = Build(new AgentAnswerResult
        {
            Sources =
            [
                new AgentSource { PostId = 10, Title = "A", Snippet = "s", Distance = 0.1f },
                new AgentSource { PostId = 20, Title = "B", Snippet = "s", Distance = 0.2f },
            ],
            Grounded = true,
        });

        var report = await sut.EvaluateAsync([new("Q?", [])]);

        var result = report.Results.Single();
        result.RetrievedCount.Should().Be(2);
        result.RetrievedPostIds.Should().BeEquivalentTo([10, 20]);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoQuestions_ReturnsEmptyReportAsync()
    {
        var sut = Build(new AgentAnswerResult { Sources = [], Grounded = false });

        var report = await sut.EvaluateAsync([]);

        report.TotalQuestions.Should().Be(0);
        report.Results.Should().BeEmpty();
        report.HitAtKRate.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static EvaluationAgent Build(AgentAnswerResult answer) =>
        new(new StubAnswerService(answer));

    private static EvaluationAgent Build(Queue<AgentAnswerResult> answers) =>
        new(new StubAnswerService(answers));

    private sealed class StubAnswerService : IAgentAnswerService
    {
        private readonly Queue<AgentAnswerResult> _answers;

        public StubAnswerService(AgentAnswerResult answer) =>
            _answers = new Queue<AgentAnswerResult>([answer]);

        public StubAnswerService(Queue<AgentAnswerResult> answers) =>
            _answers = answers;

        public Task<AgentAnswerResult> AnswerAsync(
            string question, int topK, IReadOnlyList<ConversationMessage> history) =>
            Task.FromResult(_answers.Count > 1 ? _answers.Dequeue() : _answers.Peek());
    }

    private sealed class StubAnswerQualityJudge(AnswerQualityScores scores) : IAnswerQualityJudge
    {
        public int CallCount { get; private set; }

        public Task<AnswerQualityScores> EvaluateAsync(
            string question,
            string answer,
            IReadOnlyList<AgentSource> sources)
        {
            CallCount++;
            return Task.FromResult(scores);
        }
    }

    private sealed class AdvancingTimeProvider(params long[] timestamps) : TimeProvider
    {
        private int _index;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamps[_index++];
    }
}
