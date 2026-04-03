namespace VectorSearch.Core;

/// <summary>Represents the absence of a meaningful value — a type-safe alternative to byte/bool placeholders.</summary>
public readonly struct Unit
{
    public static readonly Unit Value = default;
}
