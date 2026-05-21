using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SousChef.Core.Common;
using SousChef.Core.Interfaces;
using SousChef.Infrastructure.Data;
using SousChef.Infrastructure.Extraction;
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

        services.AddScoped<IStorageService, R2StorageService>();
        services.AddScoped<IDocumentExtractor, PdfDocumentExtractor>();

        // Stub registrations — uncommented as implementations land in Phase 3
        // services.AddScoped<IExtractionService, LlmExtractionService>();
        // services.AddScoped<IEmbeddingService, LlmEmbeddingService>();

        return services;
    }
}
