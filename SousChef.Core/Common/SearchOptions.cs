namespace SousChef.Core.Common;

public class SearchOptions
{
    public float SemanticDistanceThreshold { get; set; } = 0.5f;
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 50;
}
