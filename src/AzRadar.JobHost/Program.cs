using AzRadar.Shared;
using AzRadar.Shared.Configuration;
using AzRadar.Shared.Interfaces;
using AzRadar.JobHost;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection(CosmosDbSettings.SectionName));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection(OpenAiSettings.SectionName));
builder.Services.Configure<GitHubSettings>(builder.Configuration.GetSection(GitHubSettings.SectionName));
builder.Services.Configure<ServiceHealthProvisioningSettings>(
    builder.Configuration.GetSection(ServiceHealthProvisioningSettings.SectionName));
builder.Services.Configure<ServiceHealthEventHubSettings>(
    builder.Configuration.GetSection(ServiceHealthEventHubSettings.SectionName));

// Register shared services
builder.Services.AddAzRadarSharedServices();

// Register the Change Feed processor as a hosted service
builder.Services.AddHostedService<ChangeFeedWorker>();
builder.Services.AddHostedService<ServiceHealthIngressWorker>();
builder.Services.AddHostedService<LifecycleDeadlineBackfillWorker>();
builder.Services.AddSingleton<ScheduleWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScheduleWorker>());

var app = builder.Build();

// Initialize Cosmos DB
var cosmosDb = app.Services.GetRequiredService<ICosmosDbService>();
await cosmosDb.InitializeAsync();
await app.Services.GetRequiredService<IScheduleStore>().InitializeAsync();

// Health endpoint so App Service knows the container is alive
app.MapGet("/", () => Results.Ok(new
{
    status = "running", role = "job-host", timestamp = DateTimeOffset.UtcNow,
    azureUpdatesIngestion = "catalog-rss-v2", supportsSkipLlmAnalysis = true, supportsJobHeartbeat = true
}));

app.MapGet("/health/schedules", async (ScheduleWorker scheduler, AzRadar.Shared.Services.JobExecutionGate gate, CancellationToken ct) =>
{
    var execution = await gate.GetStateAsync(ct);
    var healthy = scheduler.LastSuccessfulPass.HasValue &&
        DateTimeOffset.UtcNow - scheduler.LastSuccessfulPass.Value < TimeSpan.FromMinutes(3) &&
        scheduler.LastError is null && !execution.Stalled;
    return Results.Json(new
    {
        status = healthy ? "healthy" : "degraded",
        version = "jobhost-schedules-v1",
        scheduler.LastSuccessfulPass,
        scheduler.LastError,
        execution
    }, statusCode: healthy ? 200 : 503);
});

app.Run();
