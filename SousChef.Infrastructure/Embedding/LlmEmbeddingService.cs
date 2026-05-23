using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SousChef.Core.Common;
using SousChef.Core.Interfaces;

namespace SousChef.Infrastructure.Embedding;

public class LlmEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<LlmEmbeddingService> _logger;
    private readonly HttpClient _httpClient;

    public LlmEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<LlmEmbeddingService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("openai");
    }

    public async Task<Result<float[]>> GenerateAsync(
        string text, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Generating embedding via {Provider}/{Model} ({Chars} chars)",
            _options.Provider, _options.ModelName, text.Length);

        try
        {
            var request = new
            {
                model = _options.ModelName,
                input = text,
                dimensions = _options.Dimensions
            };

            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add(
                "Authorization", $"Bearer {_options.ApiKey}");

            var httpResponse = await _httpClient.PostAsync("/v1/embeddings", content, ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var error = await httpResponse.Content.ReadAsStringAsync(ct);
                return Result<float[]>.Failure(
                    Error.Internal($"OpenAI API error {httpResponse.StatusCode}: {error}"));
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var embedding = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(e => e.GetSingle())
                .ToArray();

            _logger.LogInformation(
                "Embedding generated: {Dimensions} dimensions", embedding.Length);

            return Result<float[]>.Success(embedding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding generation failed");
            return Result<float[]>.Failure(
                Error.Internal($"Embedding failed: {ex.Message}"));
        }
    }

    public async Task<Result<IReadOnlyList<float[]>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            var result = await GenerateAsync(text, ct);
            if (!result.IsSuccess)
                return Result<IReadOnlyList<float[]>>.Failure(result.Error!);
            results.Add(result.Value!);
        }
        return Result<IReadOnlyList<float[]>>.Success(results);
    }
}
