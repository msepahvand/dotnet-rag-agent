namespace RagAgent.Core.Models;

public sealed record PostEmbedding(Post Post, int ChunkIndex, float[] Embedding);
