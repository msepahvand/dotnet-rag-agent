using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using VectorSearch.Core;
using VectorSearch.Core.Models;

namespace VectorSearch.S3;

public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(Kernel kernel)
    {
        _embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    public async Task<List<(int PostId, float[] Embedding)>> GenerateEmbeddingsAsync(List<Post> posts)
    {
        var embeddings = new List<(int PostId, float[] Embedding)>();

        foreach (var post in posts)
        {
            // Combine title and body for richer semantic content
            var content = $"{post.Title}\n\n{post.Body}";
            
            var embedding = await GenerateEmbeddingAsync(content);
            embeddings.Add((post.Id, embedding));

            // Log progress (optional)
            Console.WriteLine($"Generated embedding for post {post.Id}: {post.Title}");
        }

        return embeddings;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
            var result = await _embeddingGenerator.GenerateAsync([text]);
        // result is of type GeneratedEmbeddings<Embedding<float>>
        // Access the first embedding's Vector property
        return result[0].Vector.ToArray();
    }
}
