using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Amazon.BedrockAgentCore.Model;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.AgentCore;

public sealed class AgentCoreRuntimeAnswerService(
    IAgentCoreRuntimeClient runtimeClient,
    AgentCoreRuntimeOptions options) : IAgentAnswerService
{
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(120);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentAnswerResult> AnswerAsync(AgentAnswerRequest request)
    {
        using var timeout = CreateTimeout(CancellationToken.None);
        var response = await InvokeAsync(request, streaming: false, timeout.Token);

        EnsureSuccess(response);
        await using var responseStream = GetResponseStream(response);
        return await JsonSerializer.DeserializeAsync<AgentAnswerResult>(
                responseStream,
                JsonOptions,
                timeout.Token)
            ?? throw new InvalidOperationException("AgentCore Runtime returned an empty answer.");
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken);
        var response = await InvokeAsync(request, streaming: true, timeout.Token);

        EnsureSuccess(response);
        await using var responseStream = GetResponseStream(response);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            var agentEvent = JsonSerializer.Deserialize<AgentStreamEvent>(data, JsonOptions)
                ?? throw new InvalidOperationException("AgentCore Runtime returned an empty stream event.");
            yield return agentEvent;
        }
    }

    private async Task<InvokeAgentRuntimeResponse> InvokeAsync(
        AgentAnswerRequest request,
        bool streaming,
        CancellationToken cancellationToken)
    {
        if (request.SessionId.Length is < 33 or > 256)
        {
            throw new ArgumentException(
                "AgentCore Runtime session IDs must contain between 33 and 256 characters.",
                nameof(request));
        }

        var currentActivity = Activity.Current;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new RuntimeInvocationPayload(request.Question, request.TopK, request.History, streaming),
            JsonOptions);

        return await runtimeClient.InvokeAsync(
            new InvokeAgentRuntimeRequest
            {
                AgentRuntimeArn = options.RuntimeArn,
                Qualifier = "DEFAULT",
                RuntimeSessionId = request.SessionId,
                ContentType = "application/json",
                Accept = streaming ? "text/event-stream" : "application/json",
                Payload = new MemoryStream(payload),
                TraceParent = currentActivity?.IdFormat == ActivityIdFormat.W3C
                    ? currentActivity.Id
                    : null
            },
            cancellationToken);
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InvocationTimeout);
        return timeout;
    }

    private static Stream GetResponseStream(InvokeAgentRuntimeResponse response) =>
        response.Response ?? throw new InvalidOperationException("AgentCore Runtime returned no response body.");

    private static void EnsureSuccess(InvokeAgentRuntimeResponse response)
    {
        if (response.StatusCode is < 200 or >= 300)
        {
            throw new HttpRequestException(
                $"AgentCore Runtime invocation failed with status code {response.StatusCode}.");
        }
    }

    private sealed record RuntimeInvocationPayload(
        string Question,
        int TopK,
        IReadOnlyList<ConversationMessage> History,
        bool Stream);
}
