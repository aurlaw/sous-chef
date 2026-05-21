using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using SousChef.Api.Endpoints;
using SousChef.Api.Hubs;
using SousChef.Api.Workers;
using SousChef.Infrastructure;
using SousChef.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.OpenTelemetry(otel =>
        {
            otel.Endpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? "http://localhost:4317";
            otel.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "souschef-api"
            };
        });
});

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddAntiforgery();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://souschef.aurlaw.dev")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddSousChefInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ExtractionBackgroundService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "SousChef API";
        options.Theme = ScalarTheme.Default;
    });

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SousChefDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseCors("VueFrontend");
app.UseAntiforgery();
app.MapDefaultEndpoints();

app.MapUploadEndpoints();
app.MapJobEndpoints();
app.MapHub<JobStatusHub>("/hubs/jobs");

app.Run();
