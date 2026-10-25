using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using RagAgent.Agents;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.IntegrationTests;

public class MockEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;

    public MockEmbeddingService(int dimensions = 1024)
    {
        _dimensions = dimensions;
    }

    public async IAsyncEnumerable<PostEmbedding> StreamEmbeddings(
        List<Post> posts,
        int maxConcurrency = 3,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<PostEmbedding>();

        var producer = Parallel.ForEachAsync(
            posts,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (post, ct) =>
            {
                var text = $"{post.Title} {post.Body}";
                var chunks = TextChunker.Split(text);
                for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var embedding = await GenerateEmbeddingAsync(chunks[chunkIndex]);
                    await channel.Writer.WriteAsync(new PostEmbedding(post, chunkIndex, embedding), ct);
                }
            });

        _ = producer.ContinueWith(
            t => channel.Writer.Complete(t.IsFaulted ? t.Exception!.InnerException : null),
            TaskScheduler.Default);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embedding = GenerateDeterministicEmbedding(text);
        return Task.FromResult(embedding);
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(string text)
    {
        IReadOnlyList<float[]> embeddings = TextChunker.Split(text)
            .Select(GenerateDeterministicEmbedding)
            .ToList();
        return Task.FromResult(embeddings);
    }

    private float[] GenerateDeterministicEmbedding(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        var random = new Random(BitConverter.ToInt32(hash, 0));

        var embedding = new float[_dimensions];

        double sum = 0;
        for (int i = 0; i < _dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
            sum += embedding[i] * embedding[i];
        }

        float norm = (float)Math.Sqrt(sum);
        for (int i = 0; i < _dimensions; i++)
        {
            embedding[i] /= norm;
        }

        return embedding;
    }
}
