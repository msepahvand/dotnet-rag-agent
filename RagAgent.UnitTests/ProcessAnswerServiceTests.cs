using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using RagAgent.Agents.Process;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

[CollectionDefinition("SK process tests", DisableParallelization = true)]
public sealed class ProcessAnswerServiceCollection
{
}

[Collection("SK process tests")]
public sealed class ProcessAnswerServiceTests
{
    [Fact]
    public async Task AnswerAsync_WhenCriticApprovesFirstDraft_ReportsOneIterationAsync()
    {
        using var provider = Build([true], out var writer, out var critic);
        using var scope = provider.CreateScope();
        var sut = CreateService(scope);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(1);
        writer.CallCount.Should().Be(1);
        critic.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AnswerAsync_WhenCriticRequestsOneRevision_ReportsTwoIterationsAsync()
    {
        using var provider = Build([false, true], out var writer, out var critic);
        using var scope = provider.CreateScope();
        var sut = CreateService(scope);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(2);
        writer.CallCount.Should().Be(2);
        writer.Feedbacks.Should().Equal(null, "revise answer");
        critic.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AnswerAsync_OnThirdRejectedDraft_SkipsCriticAndReturnsThirdDraftAsync()
    {
        using var provider = Build([false, false, true], out var writer, out var critic);
        using var scope = provider.CreateScope();
        var sut = CreateService(scope);

        var result = await sut.AnswerAsync("Question", 5, []);

        result.Iterations.Should().Be(3);
        result.Answer.Should().Be("Draft 3");
        writer.CallCount.Should().Be(3);
        critic.CallCount.Should().Be(2);
    }

    private static ServiceProvider Build(
        IReadOnlyList<bool> verdicts,
        out StubWriter writer,
        out StubCritic critic)
    {
        writer = new StubWriter();
        critic = new StubCritic(verdicts);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IResearcherAgent, StubResearcher>();
        services.AddSingleton<IWriterAgent>(writer);
        services.AddSingleton<ICriticAgent>(critic);
        services.AddTransient(serviceProvider => new Kernel(serviceProvider));
        services.AddScoped<ProcessResultHolder>();

        return services.BuildServiceProvider();
    }

    private static ProcessAnswerService CreateService(IServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<Kernel>(),
            scope.ServiceProvider.GetRequiredService<ProcessResultHolder>());

    private sealed class StubResearcher : IResearcherAgent
    {
        public Task<ResearchResult> ResearchAsync(string question, int topK) =>
            Task.FromResult(new ResearchResult());
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
