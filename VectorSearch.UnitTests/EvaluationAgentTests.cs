using FluentAssertions;
using VectorSearch.Core;
using VectorSearch.Core.Models;
using VectorSearch.S3.Agents;

namespace VectorSearch.UnitTests;

public class EvaluationAgentTests
{
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
            string question, int topK, IReadOnlyList<ChatMessage> history) =>
            Task.FromResult(_answers.Count > 1 ? _answers.Dequeue() : _answers.Peek());
    }
}
