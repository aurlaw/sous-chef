using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SousChef.Core.Common;
using SousChef.Core.DTOs;
using SousChef.Core.Interfaces;
using SousChef.Core.Models;

namespace SousChef.Infrastructure.Extraction;

public class LlmExtractionService : IExtractionService
{
    private readonly ExtractionOptions _options;
    private readonly ILogger<LlmExtractionService> _logger;
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions DeserializeOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };

    public LlmExtractionService(
        IOptions<ExtractionOptions> options,
        ILogger<LlmExtractionService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("anthropic");
    }

    public async Task<Result<ExtractionResult>> ExtractRecipesAsync(
        string documentText, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Sending document to {Provider}/{Model} for extraction ({Chars} chars)",
            _options.Provider, _options.ModelName, documentText.Length);

        try
        {
            var response = await CallAnthropicAsync(documentText, ct);
            if (!response.IsSuccess)
                return Result<ExtractionResult>.Failure(response.Error!);

            var result = ParseResponse(response.Value!);
            if (result is null)
                return Result<ExtractionResult>.Failure(
                    Error.Internal("Failed to parse response from LLM."));

            if (result.IsNotARecipe)
            {
                _logger.LogWarning(
                    "Claude determined document is not a recipe: {Reason}",
                    result.NotARecipeReason);
            }
            else
            {
                _logger.LogInformation(
                    "Extraction successful: {Title} ({Ingredients} ingredients, {Steps} steps)",
                    result.Recipe!.Title,
                    result.Recipe.Ingredients.Count,
                    result.Recipe.Steps.Count);
            }

            return Result<ExtractionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM extraction failed");
            return Result<ExtractionResult>.Failure(
                Error.Internal($"LLM extraction failed: {ex.Message}"));
        }
    }

    private async Task<Result<string>> CallAnthropicAsync(
        string documentText, CancellationToken ct)
    {
        var request = new
        {
            model = _options.ModelName,
            max_tokens = 4096,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = documentText }
            }
        };

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var httpResponse = await _httpClient.PostAsync("/v1/messages", content, ct);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var error = await httpResponse.Content.ReadAsStringAsync(ct);
            return Result<string>.Failure(
                Error.Internal($"Anthropic API error {httpResponse.StatusCode}: {error}"));
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            _logger.LogInformation(
                "Token usage — input: {Input}, output: {Output}",
                usage.GetProperty("input_tokens").GetInt32(),
                usage.GetProperty("output_tokens").GetInt32());
        }

        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return text is not null
            ? Result<string>.Success(text)
            : Result<string>.Failure(Error.Internal("Empty response from Anthropic API."));
    }

    private static ExtractionResult? ParseResponse(string rawJson)
    {
        var cleaned = rawJson.Trim();
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

        using var doc = JsonDocument.Parse(cleaned);

        if (doc.RootElement.TryGetProperty("error", out var errorProp)
            && errorProp.GetString() == "not_a_recipe")
        {
            var reason = doc.RootElement.TryGetProperty("reason", out var r)
                ? r.GetString() ?? "Not a recipe."
                : "Not a recipe.";
            return new ExtractionResult(null, reason);
        }

        var recipe = JsonSerializer.Deserialize<RecipeDto>(cleaned, DeserializeOptions);
        if(recipe is not null)
        {
            if(recipe.CookTimeMinutes.HasValue && recipe.PrepTimeMinutes.HasValue)
                recipe = recipe with { TotalTimeMinutes = recipe.CookTimeMinutes + recipe.PrepTimeMinutes };
            return new ExtractionResult(recipe);            
        }
        else 
            return null;
    }

    private const string SystemPrompt = """
        You are a precise recipe extraction assistant. Your task is to extract structured
        recipe data from the provided text, which has been extracted from a PDF recipe document.

        Extract exactly ONE recipe and return a JSON object matching the schema below.
        Do not include any text, explanation, or markdown formatting outside the JSON object
        — return raw JSON only.

        IMPORTANT: If the document does not appear to contain a recipe (e.g. it is a shopping
        list, menu, article, table of contents, or other non-recipe content), return this exact
        JSON instead:
        { "error": "not_a_recipe", "reason": "<brief explanation of why this is not a recipe>" }

        Rules:
        - If a field cannot be determined from the text, use null for optional fields or omit it
        - Normalize ingredient units to standard abbreviations (tsp, tbsp, cup, oz, lb, g, kg, ml, l)
        - Infer cuisine_type from ingredients, technique, and dish name if not explicitly stated
        - Infer difficulty: "easy" (minimal technique, common ingredients), "medium" (some technique
          required), "hard" (advanced technique, specialized equipment or ingredients)
        - Tags should include cuisine type, meal type, primary ingredients, dietary notes
          (e.g. vegetarian, gluten-free) where evident
        - Step numbers must be sequential starting from 1
        - prep_time_minutes and cook_time_minutes are integers representing minutes; null if not stated
        - servings is an integer; null if not stated

        Return this exact JSON structure for valid recipes:
        {
          "title": "string",
          "description": "string or null",
          "cuisine_type": "string or null",
          "course": "one of: appetizer, soup, salad, main, side, dessert, breakfast, snack, beverage, or null",
          "prep_time_minutes": "integer or null",
          "cook_time_minutes": "integer or null",
          "servings": "integer or null",
          "difficulty": "easy | medium | hard",
          "tags": ["string"],
          "ingredients": [
            { "name": "string", "quantity": "string or null", "unit": "string or null", "notes": "string or null" }
          ],
          "steps": [
            { "step_number": 1, "instruction": "string" }
          ]
        }
        """;
}
