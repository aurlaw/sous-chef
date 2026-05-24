namespace SousChef.Infrastructure.Data;

public class RecipeSearchResult
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CuisineType { get; set; }
    public string? Course { get; set; }
    public string? Difficulty { get; set; }
    public int? TotalTimeMinutes { get; set; }
    public int? Servings { get; set; }
    public string[] Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public float Distance { get; set; }
}
