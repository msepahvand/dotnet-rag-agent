using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RagAgent.Api.Services;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.UnitTests;

public class IngestionBackgroundServiceTests
{
    // ── First run (tracker not seeded) ────────────────────────────────────────
    [Fact]
    public async Task RunOnceAsync_WhenTrackerNotSeeded_SeedsTrackerWithoutIndexingAsync()
    {
        var postService = new StubPostService([Post(1), Post(2)]);
        var indexingService = new CapturingIndexingService();
        var tracker = new IngestionTracker();
        var sut = Build(tracker, postService, indexingService);

        await sut.RunOnceAsync(CancellationToken.None);

        tracker.IsSeeded.Should().BeTrue();
        tracker.IndexedCount.Should().Be(2);
        indexingService.IndexedPosts.Should().BeEmpty();  // no indexing on seed-only run
    }

    // ── Subsequent runs ───────────────────────────────────────────────────────
    [Fact]
    public async Task RunOnceAsync_WhenAllPostsAlreadyIndexed_IndexesNothingAsync()
    {
        var tracker = SeededTracker([1, 2]);
        var postService = new StubPostService([Post(1), Post(2)]);
        var indexingService = new CapturingIndexingService();
        var sut = Build(tracker, postService, indexingService);

        await sut.RunOnceAsync(CancellationToken.None);

        indexingService.IndexedPosts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WhenNewPostAppears_IndexesOnlyNewPostAsync()
    {
        var tracker = SeededTracker([1, 2]);
        var postService = new StubPostService([Post(1), Post(2), Post(3)]);
        var indexingService = new CapturingIndexingService();
        var sut = Build(tracker, postService, indexingService);

        await sut.RunOnceAsync(CancellationToken.None);

        indexingService.IndexedPosts.Should().ContainSingle(p => p.Id == 3);
    }

    [Fact]
    public async Task RunOnceAsync_WhenMultipleNewPosts_IndexesAllNewAsync()
    {
        var tracker = SeededTracker([1]);
        var postService = new StubPostService([Post(1), Post(2), Post(3)]);
        var indexingService = new CapturingIndexingService();
        var sut = Build(tracker, postService, indexingService);

        await sut.RunOnceAsync(CancellationToken.None);

        indexingService.IndexedPosts.Select(p => p.Id).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public async Task RunOnceAsync_AfterIndexingNewPosts_TracksThemSoNextRunSkipsThemAsync()
    {
        var tracker = SeededTracker([1]);
        var postService = new StubPostService([Post(1), Post(2)]);
        var sut = Build(tracker, postService, new CapturingIndexingService());

        await sut.RunOnceAsync(CancellationToken.None);
        await sut.RunOnceAsync(CancellationToken.None);  // second run — post 2 now tracked

        tracker.IsIndexed(2).Should().BeTrue();
    }

    [Fact]
    public async Task RunOnceAsync_WhenPostServiceThrows_DoesNotPropagateExceptionAsync()
    {
        var tracker = SeededTracker([1]);
        var sut = Build(tracker, new ThrowingPostService(), new CapturingIndexingService());

        var act = () => sut.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static Post Post(int id) =>
        new(id, UserId: 1, Title: $"Post {id}", Body: "Body.");

    private static IngestionTracker SeededTracker(IEnumerable<int> ids)
    {
        var tracker = new IngestionTracker();
        tracker.MarkIndexed(ids);
        return tracker;
    }

    private static IngestionBackgroundService Build(
        IngestionTracker tracker,
        IPostService postService,
        IPostIndexingService indexingService)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPostService>(_ => postService);
        services.AddScoped<IPostIndexingService>(_ => indexingService);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new IngestionBackgroundService(
            scopeFactory,
            tracker,
            new ConfigurationBuilder().Build(),
            NullLogger<IngestionBackgroundService>.Instance);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────
    private sealed class StubPostService(IReadOnlyList<Post> posts) : IPostService
    {
        public Task<List<Post>> GetAllPostsAsync() => Task.FromResult(posts.ToList());
        public Task<Post?> GetPostByIdAsync(int id) => Task.FromResult(posts.FirstOrDefault(p => p.Id == id));
    }

    private sealed class ThrowingPostService : IPostService
    {
        public Task<List<Post>> GetAllPostsAsync() => throw new InvalidOperationException("HN is down.");
        public Task<Post?> GetPostByIdAsync(int id) => throw new InvalidOperationException("HN is down.");
    }

    private sealed class CapturingIndexingService : IPostIndexingService
    {
        public List<Post> IndexedPosts { get; } = [];

        public Task<IndexAllPostsResult> IndexAllAsync() =>
            Task.FromResult(new IndexAllPostsResult(0));

        public Task<IndexAllPostsResult> IndexPostsAsync(IReadOnlyList<Post> posts)
        {
            IndexedPosts.AddRange(posts);
            return Task.FromResult(new IndexAllPostsResult(posts.Count));
        }

        public Task<IndexSinglePostResult?> IndexSingleAsync(int id) =>
            Task.FromResult<IndexSinglePostResult?>(null);
    }
}
