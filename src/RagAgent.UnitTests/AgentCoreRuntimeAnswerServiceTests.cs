using System.Text;
using System.Text.Json;
using Amazon.BedrockAgentCore.Model;
using FluentAssertions;
using RagAgent.AgentCore;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public sealed class AgentCoreRuntimeAnswerServiceTests
{
    [Fact]
    public async Task AnswerAsync_InvokesDefaultEndpointWithSessionAndRequestPayloadAsync()
    {
        var runtimeClient = new FakeAgentCoreRuntimeClient(
            """{"answer":"grounded","grounded":true,"sources":[],"citations":[],"toolsUsed":["search_posts"],"iterations":1}""");
        var sut = CreateService(runtimeClient);
        var request = new AgentAnswerRequest("What is RAG?", 4, [new ConversationMessage("user", "Earlier")], new string('a', 64));

        var answer = await sut.AnswerAsync(request);

        answer.Answer.Should().Be("grounded");
        runtimeClient.LastRequest.Should().NotBeNull();
        runtimeClient.LastRequest!.AgentRuntimeArn.Should().Be("runtime-arn");
        runtimeClient.LastRequest.Qualifier.Should().Be("DEFAULT");
        runtimeClient.LastRequest.RuntimeSessionId.Should().Be(request.SessionId);
        runtimeClient.LastRequest.ContentType.Should().Be("application/json");
        runtimeClient.LastPayload.Should().Contain("\"question\":\"What is RAG?\"");
        runtimeClient.LastPayload.Should().Contain("\"topK\":4");
        runtimeClient.LastPayload.Should().Contain("\"history\":[{\"role\":\"user\",\"content\":\"Earlier\"}]");
        runtimeClient.LastPayload.Should().Contain("\"stream\":false");
    }

    [Fact]
    public async Task StreamAsync_ParsesAgentCoreServerSentEventsAsync()
    {
        var runtimeClient = new FakeAgentCoreRuntimeClient(
            "data: {\"type\":\"token\",\"content\":\"hello\"}\n\n" +
            "data: {\"type\":\"done\",\"grounded\":true,\"sources\":[]}\n\n");
        var sut = CreateService(runtimeClient);

        var events = await sut.StreamAsync(CreateRequest()).ToListAsync();

        events.Select(agentEvent => agentEvent.Type).Should().Equal("token", "done");
        events[0].Content.Should().Be("hello");
        runtimeClient.LastRequest!.Accept.Should().Be("text/event-stream");
        runtimeClient.LastPayload.Should().Contain("\"stream\":true");
    }

    [Fact]
    public async Task AnswerAsync_WhenRuntimeReturnsFailureStatus_ThrowsAsync()
    {
        var runtimeClient = new FakeAgentCoreRuntimeClient("{}") { StatusCode = 502 };
        var sut = CreateService(runtimeClient);

        var act = () => sut.AnswerAsync(CreateRequest());

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*502*");
    }

    private static AgentCoreRuntimeAnswerService CreateService(FakeAgentCoreRuntimeClient runtimeClient) =>
        new(runtimeClient, new AgentCoreRuntimeOptions("runtime-arn"));

    private static AgentAnswerRequest CreateRequest() =>
        new("Question", 5, [], new string('b', 64));

    private sealed class FakeAgentCoreRuntimeClient(string responseBody) : IAgentCoreRuntimeClient
    {
        public int StatusCode { get; set; } = 200;
        public InvokeAgentRuntimeRequest? LastRequest { get; private set; }
        public string LastPayload { get; private set; } = string.Empty;

        public async Task<InvokeAgentRuntimeResponse> InvokeAsync(
            InvokeAgentRuntimeRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            using var reader = new StreamReader(request.Payload);
            LastPayload = await reader.ReadToEndAsync(cancellationToken);

            return new InvokeAgentRuntimeResponse
            {
                StatusCode = StatusCode,
                ContentType = request.Accept,
                Response = new MemoryStream(Encoding.UTF8.GetBytes(responseBody))
            };
        }
    }
}
