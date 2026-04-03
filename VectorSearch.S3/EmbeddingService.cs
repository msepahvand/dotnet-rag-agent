using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
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
                var content = $"{post.Title}\n\n{post.Body}";
                var embedding = await GenerateEmbeddingAsync(content);
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

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var result = await _embeddingGenerator.GenerateAsync([text]);
        return result[0].Vector.ToArray();
    }
}
