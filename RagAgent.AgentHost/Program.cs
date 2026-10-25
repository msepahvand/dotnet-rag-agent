using System.Text.Json;
using Amazon.Extensions.NETCore.Setup;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Agents;
using RagAgent.Core;
using RagAgent.Core.Models;
using RagAgent.HackerNews;
using RagAgent.Qdrant;
using RagAgent.Redis;
using RagAgent.S3Vectors;

namespace RagAgent.AgentHost;

public class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHackerNewsDataSource(builder.Configuration);
        builder.Services.AddVectorSearch(builder.Configuration);
        RegisterVectorStore(builder.Services, builder.Configuration);

        var app = builder.Build();
        app.MapGet("/ping", () => Results.Ok(new { status = "Healthy" }));
        app.MapPost("/invocations", HandleInvocationAsync);
        await app.RunAsync();
    }

    private static void RegisterVectorStore(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = VectorSearchOptionsValidator.ParseProvider(configuration);
        switch (provider)
        {
            case VectorStoreProvider.Qdrant:
                services.AddQdrantVectorStore();
                break;
            case VectorStoreProvider.Redis:
                services.AddRedisVectorStore();
                break;
            case VectorStoreProvider.S3Vectors:
                services.AddS3VectorsVectorStore();
                break;
            default:
                throw new InvalidOperationException($"Unsupported vector-store provider '{provider}'.");
        }
    }

    private static async Task HandleInvocationAsync(
        HttpContext context,
        IAgentAnswerService answerService)
    {
        var payload = await context.Request.ReadFromJsonAsync<AgentInvocationPayload>(
            JsonOptions,
            context.RequestAborted);
        if (payload is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (payload.HealthCheck)
        {
            await context.Response.WriteAsJsonAsync(
                new AgentHostHealthResponse("Healthy", Environment.GetEnvironmentVariable("APP_VERSION") ?? "local"),
                JsonOptions,
                context.RequestAborted);
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.Question))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var request = new AgentAnswerRequest(
            payload.Question,
            TopKNormaliser.Normalise(payload.TopK),
            payload.History,
            context.Request.Headers["X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"].FirstOrDefault()
                ?? ConversationSessionId.Create(Guid.NewGuid().ToString()));

        if (!payload.Stream)
        {
            var answer = await answerService.AnswerAsync(request);
            await context.Response.WriteAsJsonAsync(answer, JsonOptions, context.RequestAborted);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        await foreach (var agentEvent in answerService.StreamAsync(request, context.RequestAborted))
        {
            var json = JsonSerializer.Serialize(agentEvent, JsonOptions);
            await context.Response.WriteAsync($"data: {json}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
}

public sealed record AgentInvocationPayload
{
    public string Question { get; init; } = string.Empty;
    public int TopK { get; init; } = 5;
    public IReadOnlyList<ConversationMessage> History { get; init; } = [];
    public bool Stream { get; init; }
    public bool HealthCheck { get; init; }
}

public sealed record AgentHostHealthResponse(string Status, string AppVersion);
