using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public class MockEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;

    public MockEmbeddingService(int dimensions = 1024)
    {
        _dimensions = dimensions;
    }

    public async IAsyncEnumerable<(int PostId, float[] Embedding)> StreamEmbeddings(
        List<Post> posts,
        int maxConcurrency = 3,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<(int PostId, float[] Embedding)>();

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
                var embedding = await GenerateEmbeddingAsync(text);
                await channel.Writer.WriteAsync((post.Id, embedding), ct);
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
