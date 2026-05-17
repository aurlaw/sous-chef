#:sdk Aspire.AppHost.Sdk@13.3.3
#:package Aspire.Hosting.PostgreSQL@13.3.3
#:package Aspire.Hosting.JavaScript@13.3.3
#:project SousChef.Api/SousChef.Api.csproj

var builder = DistributedApplication.CreateBuilder(args);

// Postgres with pgvector
var db = builder.AddPostgres("db")
    .WithImage("pgvector/pgvector", "pg16")
    .WithHostPort(55432)
    .WithDataVolume("postgres_data");

var souschefDb = db.AddDatabase("souschef");

// MinIO (S3-compatible local storage)
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "souschef")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "souschef_secret")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithVolume("minio_data", "/data");

// API (includes BackgroundService worker)
var api = builder.AddProject<Projects.SousChef_Api>("api")
    .WithReference(souschefDb)
    .WaitFor(souschefDb);

// Vue frontend
builder.AddViteApp("web", "./sous-chef-web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
