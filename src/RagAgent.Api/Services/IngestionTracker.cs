using System.Collections.Concurrent;
using RagAgent.Core;

namespace RagAgent.Api.Services;

/// <summary>
/// Singleton that tracks which post IDs have been indexed in the current process lifetime.
/// Used by <see cref="IngestionBackgroundService"/> to detect new posts on each poll,
/// and seeded by <see cref="IndexingStartupService"/> after the initial index pass.
/// </summary>
public sealed class IngestionTracker
{
    private readonly ConcurrentDictionary<int, Unit> _indexedIds = new();

    public bool IsSeeded => !_indexedIds.IsEmpty;

    public bool IsIndexed(int postId) => _indexedIds.ContainsKey(postId);

    public void MarkIndexed(IEnumerable<int> postIds)
    {
        foreach (var id in postIds)
        {
            _indexedIds.TryAdd(id, default);
        }
    }

    public int IndexedCount => _indexedIds.Count;
}
