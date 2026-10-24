using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RagAgent.AgentHost;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.IntegrationTests;

public sealed class AgentHostIntegrationTests
{
    [Fact]
    public async Task Invocations_ReturnsAgentAnswerFromPipelineContractAsync()
    {
        await using var factory = new AgentHostWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/invocations",
            new AgentInvocationPayload
            {
                Question = "What is RAG?",
                TopK = 5,
                History = [new ConversationMessage("user", "Earlier")]
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await response.Content.ReadFromJsonAsync<AgentAnswerResult>();
        answer.Should().NotBeNull();
        answer!.Answer.Should().Be("AgentHost test answer");
        answer.Grounded.Should().BeTrue();
    }

    [Fact]
    public async Task Ping_ReturnsHealthyStatusAsync()
    {
        await using var factory = new AgentHostWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"Healthy\"");
    }

    [Fact]
    public async Task Invocations_WhenStreaming_ReturnsAgentEventsAsServerSentEventsAsync()
    {
        await using var factory = new AgentHostWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/invocations",
            new AgentInvocationPayload { Question = "What is RAG?", Stream = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(frame => JsonSerializer.Deserialize<AgentStreamEvent>(
                frame["data: ".Length..],
                new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .ToArray();

        events.Should().NotContainNulls();
        events.Select(agentEvent => agentEvent!.Type).Should().Equal("token", "done");
        events[0]!.Content.Should().Be("streamed");
        events[1]!.Grounded.Should().BeFalse();
    }

    private sealed class AgentHostWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWS:EmbeddingModelId"] = "cohere.embed-english-v3",
                    ["AWS:ChatModelId"] = "us.anthropic.claude-sonnet-4-6",
                    ["VectorStore:Provider"] = "Qdrant",
                    ["VectorStore:Qdrant:Url"] = "http://localhost:6333",
                    ["VectorStore:Qdrant:CollectionName"] = "test_posts",
                    ["VectorStore:Qdrant:VectorSize"] = "1024",
                    ["DataSource:Provider"] = "HackerNews"
                }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgentAnswerService>();
                services.AddSingleton<IAgentAnswerService, StubAgentAnswerService>();
            });

            builder.UseEnvironment("Testing");
        }
    }

    private sealed class StubAgentAnswerService : IAgentAnswerService
    {
        public Task<AgentAnswerResult> AnswerAsync(AgentAnswerRequest request) =>
            Task.FromResult(new AgentAnswerResult
            {
                Answer = "AgentHost test answer",
                Grounded = true
            });

        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            AgentAnswerRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return AgentStreamEvent.ForToken("streamed");
            yield return AgentStreamEvent.ForDone(false, []);
        }
    }
}
