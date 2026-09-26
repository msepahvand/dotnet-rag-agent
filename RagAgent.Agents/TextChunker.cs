namespace RagAgent.Agents;

internal static class TextChunker
{
    internal const int MaximumChunkLength = 2048;
    private const int OverlapLength = 200;
    private const int MinimumBoundarySearchLength = MaximumChunkLength / 2;

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + MaximumChunkLength, text.Length);
            end = AvoidSplittingSurrogatePair(text, end);

            if (end < text.Length)
            {
                var boundary = FindWhitespaceBoundary(text, start, end);
                if (boundary > start)
                {
                    end = boundary + 1;
                }
            }

            chunks.Add(text[start..end]);
            if (end == text.Length)
            {
                break;
            }

            start = FindNextChunkStart(text, start, end);
        }

        return chunks;
    }

    private static int FindWhitespaceBoundary(string text, int start, int end)
    {
        var minimumBoundary = Math.Max(start + MinimumBoundarySearchLength, end - MaximumChunkLength);
        for (var index = end - 1; index >= minimumBoundary; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNextChunkStart(string text, int start, int end)
    {
        var nextStart = Math.Max(start + 1, end - OverlapLength);
        while (nextStart < end && !char.IsWhiteSpace(text[nextStart]))
        {
            nextStart++;
        }

        while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart]))
        {
            nextStart++;
        }

        return AvoidStartingWithLowSurrogate(text, nextStart);
    }

    private static int AvoidSplittingSurrogatePair(string text, int index) =>
        index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])
            ? index - 1
            : index;

    private static int AvoidStartingWithLowSurrogate(string text, int index) =>
        index > 0 && index < text.Length && char.IsLowSurrogate(text[index])
            ? index + 1
            : index;
}
