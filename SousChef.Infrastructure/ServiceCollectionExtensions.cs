using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SousChef.Core.Common;
using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;
using SousChef.Infrastructure.Embedding;
using SousChef.Infrastructure.Extraction;
using SousChef.Infrastructure.Search;
using SousChef.Infrastructure.Storage;

namespace SousChef.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSousChefInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("souschef");
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStr);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddDbContext<SousChefDbContext>(options =>
            options.UseNpgsql(dataSource, o =>
            {
                o.UseVector();
                o.EnableRetryOnFailure(3);
            }));

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<ExtractionOptions>(configuration.GetSection("Extraction"));
        services.Configure<EmbeddingOptions>(configuration.GetSection("Embedding"));
        services.Configure<SearchOptions>(configuration.GetSection("Search"));

        services.AddHttpClient("anthropic", client =>
        {
            client.BaseAddress = new Uri(
                configuration["Extraction:Endpoint"] ?? "https://api.anthropic.com");
            client.Timeout = TimeSpan.FromSeconds(180);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(180);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(170);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(360);
            options.Retry.MaxRetryAttempts = 1;
        });

        services.AddHttpClient("openai", client =>
        {
            client.BaseAddress = new Uri(
                configuration["Embedding:Endpoint"] ?? "https://api.openai.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddStandardResilienceHandler();

        services.AddScoped<IStorageService, R2StorageService>();
        services.AddScoped<IDocumentExtractor, PdfDocumentExtractor>();
        services.AddScoped<IExtractionService, LlmExtractionService>();
        services.AddScoped<IEmbeddingService, LlmEmbeddingService>();
        services.AddScoped<IRecipeSearchService, RecipeSearchService>();

        services.AddMemoryCache();

        return services;
    }
}
