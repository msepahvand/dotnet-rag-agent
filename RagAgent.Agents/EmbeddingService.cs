using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Agents;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
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
                var content = $"{post.Title}\n\n{post.Body}";
                var chunks = TextChunker.Split(content);
                var embeddings = await GenerateBatchAsync(chunks, "search_document", ct);

                for (var chunkIndex = 0; chunkIndex < embeddings.Count; chunkIndex++)
                {
                    await channel.Writer.WriteAsync(
                        new PostEmbedding(post, chunkIndex, embeddings[chunkIndex]),
                        ct);
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
        => GenerateAsync(text, "search_query");

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(string text)
        => GenerateBatchAsync(TextChunker.Split(text), "search_document");

    private async Task<float[]> GenerateAsync(
        string text,
        string inputType,
        CancellationToken cancellationToken = default)
    {
        var result = await GenerateBatchAsync([text], inputType, cancellationToken);
        return result[0];
    }

    private async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        string inputType,
        CancellationToken cancellationToken = default)
    {
        var options = new EmbeddingGenerationOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["input_type"] = inputType
            }
        };
        var result = await _embeddingGenerator.GenerateAsync(texts, options, cancellationToken);
        return result.Select(embedding => embedding.Vector.ToArray()).ToList();
    }
}
