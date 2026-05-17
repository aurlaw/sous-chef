using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SousChef.Infrastructure;
using SousChef.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddSousChefInfrastructure(builder.Configuration);

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
app.MapDefaultEndpoints();

app.Run();
