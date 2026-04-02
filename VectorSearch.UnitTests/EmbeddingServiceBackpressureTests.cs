using FluentAssertions;
using Microsoft.Extensions.AI;
using VectorSearch.Core.Models;
using VectorSearch.S3;

namespace VectorSearch.UnitTests;

public class EmbeddingServiceBackpressureTests
{
    private static List<Post> CreatePosts(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Post(i, 1, $"Title {i}", $"Body {i}"))
            .ToList();

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var results = new List<T>();
        await foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }

    [Fact]
    public async Task StreamEmbeddings_NeverExceedsMaxConcurrency()
    {
        const int maxConcurrency = 2;
        var concurrentCount = 0;
        var maxObserved = 0;

        var generator = new FakeEmbeddingGenerator(async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            int observed;
            do
            { observed = maxObserved; }
            while (observed < current && Interlocked.CompareExchange(ref maxObserved, current, observed) != observed);

            await Task.Delay(30);
            Interlocked.Decrement(ref concurrentCount);
            return [0.1f, 0.2f, 0.3f];
        });

        var service = new EmbeddingService(generator);
        await CollectAsync(service.StreamEmbeddings(CreatePosts(10), maxConcurrency));

        maxObserved.Should().BeLessThanOrEqualTo(
            maxConcurrency,
            because: $"Parallel.ForEachAsync caps concurrent Bedrock calls at {maxConcurrency}");
    }

    [Fact]
    public async Task StreamEmbeddings_EmitsResultsProgressively_NotAllAtEnd()
    {
        var emitTimestamps = new List<long>();
        var generator = new FakeEmbeddingGenerator(async _ =>
        {
            await Task.Delay(20);
            return [0.1f];
        });

        var service = new EmbeddingService(generator);

        await foreach (var item in service.StreamEmbeddings(CreatePosts(5), maxConcurrency: 5))
        {
            emitTimestamps.Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        emitTimestamps.Should().HaveCount(5);
        var spread = emitTimestamps.Max() - emitTimestamps.Min();
        spread.Should().BeLessThan(
            80,
            because: "all 5 embeddings run in parallel so results arrive within the same window");
    }

    [Fact]
    public async Task StreamEmbeddings_ReturnsAllResults()
    {
        var generator = new FakeEmbeddingGenerator(_ => Task.FromResult<float[]>([1.0f, 2.0f]));
        var service = new EmbeddingService(generator);

        var results = await CollectAsync(service.StreamEmbeddings(CreatePosts(5)));

        results.Should().HaveCount(5);
        results.Select(r => r.PostId).Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task StreamEmbeddings_WithMaxConcurrencyOne_ProcessesAllPosts()
    {
        var generator = new FakeEmbeddingGenerator(async _ =>
        {
            await Task.Delay(5);
            return [0.1f];
        });

        var service = new EmbeddingService(generator);
        var results = await CollectAsync(service.StreamEmbeddings(CreatePosts(4), maxConcurrency: 1));

        results.Should().HaveCount(4);
        results.Select(r => r.PostId).Should().BeEquivalentTo([1, 2, 3, 4]);
    }

    private sealed class FakeEmbeddingGenerator(Func<string, Task<float[]>> generate)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<Embedding<float>>();
            foreach (var value in values)
            {
                results.Add(new Embedding<float>(await generate(value)));
            }

            return new GeneratedEmbeddings<Embedding<float>>(results);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
