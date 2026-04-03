namespace VectorSearch.Core;

public static class TopKNormaliser
{
    public const int Default = 5;
    public const int Max = 10;

    public static int Normalise(int topK) =>
        topK <= 0 ? Default : Math.Min(topK, Max);
}
