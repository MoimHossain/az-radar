using AzRadar.Shared;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.JobHost;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection(CosmosDbSettings.SectionName));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection(OpenAiSettings.SectionName));

// Register shared services
builder.Services.AddAzRadarSharedServices();

// Register the Change Feed processor as a hosted service
builder.Services.AddHostedService<ChangeFeedWorker>();

var app = builder.Build();

// Initialize Cosmos DB
var cosmosDb = app.Services.GetRequiredService<ICosmosDbService>();
await cosmosDb.InitializeAsync();

// Health endpoint so App Service knows the container is alive
app.MapGet("/", () => Results.Ok(new { status = "running", role = "job-host", timestamp = DateTimeOffset.UtcNow }));

app.Run();
