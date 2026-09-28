using FluentAssertions;
using RagAgent.Agents.Workflow;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public sealed class AgentAnswerWorkflowServiceTests
{
    [Fact]
    public async Task AnswerAsync_WhenCriticApprovesFirstDraft_ReportsOneIterationAsync()
    {
        var writer = new StubWriter();
        var critic = new StubCritic([true]);
        var sut = CreateService(writer, critic);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(1);
        writer.CallCount.Should().Be(1);
        critic.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AnswerAsync_WhenCriticRequestsOneRevision_ReportsTwoIterationsAsync()
    {
        var writer = new StubWriter();
        var critic = new StubCritic([false, true]);
        var sut = CreateService(writer, critic);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(2);
        writer.CallCount.Should().Be(2);
        writer.Feedbacks.Should().Equal(null, "revise answer");
        critic.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AnswerAsync_OnThirdRejectedDraft_SkipsCriticAndReturnsThirdDraftAsync()
    {
        var writer = new StubWriter();
        var critic = new StubCritic([false, false]);
        var sut = CreateService(writer, critic);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(3);
        result.Answer.Should().Be("Draft 3");
        writer.CallCount.Should().Be(3);
        critic.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AnswerAsync_NormalisesTopKBeforeResearchAsync()
    {
        var researcher = new StubResearcher();
        var sut = CreateService(new StubWriter(), new StubCritic([true]), researcher);

        await sut.AnswerAsync("Question", 100, []);

        researcher.TopK.Should().Be(TopKNormaliser.Max);
    }

    private static AgentAnswerWorkflowService CreateService(
        StubWriter writer,
        StubCritic critic,
        StubResearcher? researcher = null) =>
        new(researcher ?? new StubResearcher(), writer, critic);

    private sealed class StubResearcher : IResearcherAgent
    {
        public int TopK { get; private set; }

        public Task<ResearchResult> ResearchAsync(string question, int topK)
        {
            TopK = topK;
            return Task.FromResult(new ResearchResult());
        }
    }

    private sealed class StubWriter : IWriterAgent
    {
        public int CallCount { get; private set; }
        public List<string?> Feedbacks { get; } = [];

        public Task<AgentAnswerResult> WriteAsync(
            string question,
            ResearchResult research,
            IReadOnlyList<ConversationMessage> history,
            string? criticFeedback = null)
        {
            CallCount++;
            Feedbacks.Add(criticFeedback);
            return Task.FromResult(new AgentAnswerResult { Answer = $"Draft {CallCount}" });
        }

        public async IAsyncEnumerable<string> StreamAsync(
            string question,
            ResearchResult research,
            IReadOnlyList<ConversationMessage> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubCritic(IReadOnlyList<bool> verdicts) : ICriticAgent
    {
        public int CallCount { get; private set; }

        public Task<CriticResult> EvaluateAsync(
            string question,
            AgentAnswerResult answer,
            ResearchResult research)
        {
            var approved = verdicts[CallCount++];
            return Task.FromResult(new CriticResult
            {
                Approved = approved,
                Feedback = approved ? string.Empty : "revise answer",
            });
        }
    }
}
