using System.Text.Json;

namespace SousChef.Core.Models;

public enum PipelineStage
{
    Download,
    DocumentExtraction,
    RecipeValidation,
    LlmExtraction,
    NotARecipe,
    JsonParsing
}

public record PipelineError(PipelineStage Stage, string Message, string? Detail = null)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}
