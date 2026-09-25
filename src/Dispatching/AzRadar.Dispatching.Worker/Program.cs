using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Worker;
using AzRadar.Shared.Configuration;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<DispatchingCosmosSettings>(
    builder.Configuration.GetSection(DispatchingCosmosSettings.SectionName));
builder.Services.Configure<DispatchingServiceBusSettings>(
    builder.Configuration.GetSection(DispatchingServiceBusSettings.SectionName));
builder.Services.Configure<TeamsBotSettings>(
    builder.Configuration.GetSection(TeamsBotSettings.SectionName));
builder.Services.Configure<TeamsDispatchSettings>(
    builder.Configuration.GetSection(TeamsDispatchSettings.SectionName));
builder.Services.Configure<AzureDevOpsWikiSettings>(
    builder.Configuration.GetSection(AzureDevOpsWikiSettings.SectionName));
builder.Services.AddDispatchingPersistence();
builder.Services.AddDispatchingServiceBus();
builder.Services.AddHttpClient<AzRadar.Shared.Interfaces.IAzureDevOpsWikiService, AzRadar.Shared.Services.AzureDevOpsWikiService>();
builder.Services.AddSingleton<ServiceHealthWikiRenderer>();
builder.Services.AddHostedService<DeliveryIntentOutboxWorker>();
builder.Services.AddHostedService<AzureDevOpsWikiDeliveryWorker>();
builder.Services.AddHostedService<AzureDevOpsWikiReconciliationWorker>();

if (builder.Configuration.GetValue($"{TeamsDispatchSettings.SectionName}:Enabled", true))
{
    builder.Services.AddSingleton<ServiceHealthAdaptiveCardRenderer>();
    builder.Services.AddSingleton<IStorage, MemoryStorage>();
    builder.AddAgentApplicationOptions();
    builder.AddAgent<TeamsDispatchAgent>();
    builder.Services.AddHostedService<TeamsDeliveryWorker>();
}

var app = builder.Build();
app.UseStaticFiles();
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    role = "service-health-dispatch-worker",
    timestamp = DateTimeOffset.UtcNow
}));
app.Run();
