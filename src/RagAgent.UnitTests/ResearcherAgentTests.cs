using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.AI;
using RagAgent.Agents;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public sealed class ResearcherAgentTests
{
    [Fact]
    public async Task ResearchAsync_MergesMultipleSearchesAndClampsResultsToRequestedTopKAsync()
    {
        var chatClient = new ScriptedToolChatClient(
        [
            new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
            [
                SearchCall("first", 10),
                SearchCall("second", 10),
            ])),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Research complete.")),
        ]);
        var vectorService = new StubVectorService(query => query switch
        {
            "first" =>
            [
                SearchResult(1, "First result", 0.4),
                SearchResult(2, "Second result", 0.6),
            ],
            "second" =>
            [
                SearchResult(1, "First result", 0.1),
                SearchResult(3, "Third result", 0.2),
            ],
            _ => [],
        });
        var sut = new ResearcherAgent(
            chatClient.AsBuilder().UseFunctionInvocation().Build(),
            new SemanticSearchPlugin(vectorService, new StubPostService()));

        var result = await sut.ResearchAsync("Original question", topK: 2);

        result.Sources.Select(source => source.PostId).Should().Equal(1, 3);
        result.Sources[0].Distance.Should().Be(0.1);
        result.ToolsUsed.Should().ContainSingle().Which.Should().Be("search_posts");
        vectorService.TopKValues.Should().Equal(2, 2);
    }

    [Fact]
    public async Task ResearchAsync_WhenModelDoesNotCallSearch_FallsBackToOriginalQuestionAsync()
    {
        var chatClient = new ScriptedToolChatClient(
        [
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "I have no tool calls.")),
        ]);
        var vectorService = new StubVectorService(_ =>
        [
            SearchResult(7, "Fallback result", 0.25),
        ]);
        var sut = new ResearcherAgent(
            chatClient.AsBuilder().UseFunctionInvocation().Build(),
            new SemanticSearchPlugin(vectorService, new StubPostService()));

        var result = await sut.ResearchAsync("Original question", topK: 4);

        vectorService.Queries.Should().Equal("Original question");
        vectorService.TopKValues.Should().Equal(4);
        result.Sources.Select(source => source.PostId).Should().Equal(7);
        result.ToolsUsed.Should().Contain("search_posts");
    }

    private static FunctionCallContent SearchCall(string question, int topK) =>
        new(
            Guid.NewGuid().ToString("N"),
            "search_posts",
            new Dictionary<string, object?>
            {
                ["question"] = question,
                ["topK"] = topK,
            });

    private static SearchResult SearchResult(int postId, string title, double distance) =>
        new() { PostId = postId, Title = title, Distance = distance };

    private sealed class ScriptedToolChatClient(IEnumerable<ChatResponse> responses) : IChatClient
    {
        private readonly ConcurrentQueue<ChatResponse> _responses = new(responses);

        public ChatClientMetadata Metadata { get; } = new("ScriptedToolChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException("No scripted chat response remains.");
            }

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class StubVectorService(
        Func<string, List<SearchResult>> search) : IVectorService
    {
        public List<string> Queries { get; } = [];
        public List<int> TopKValues { get; } = [];

        public Task<List<SearchResult>> SemanticSearchAsync(string query, int topK = 10)
        {
            lock (Queries)
            {
                Queries.Add(query);
                TopKValues.Add(topK);
            }

            return Task.FromResult(search(query).Take(topK).ToList());
        }

        public Task IndexPostAsync(Post post, IReadOnlyList<float[]> embeddings) =>
            throw new NotSupportedException();

        public Task IndexPostsBatchAsync(List<PostEmbedding> embeddings) =>
            throw new NotSupportedException();

        public Task<bool> IsIndexEmptyAsync() => throw new NotSupportedException();

        public Task EnsureInitializedAsync() => throw new NotSupportedException();
    }

    private sealed class StubPostService : IPostService
    {
        public Task<Post?> GetPostByIdAsync(int id) =>
            Task.FromResult<Post?>(new Post(id, 1, $"Post {id}", $"Body for {id}."));

        public Task<List<Post>> GetAllPostsAsync() =>
            throw new NotSupportedException();
    }
}
