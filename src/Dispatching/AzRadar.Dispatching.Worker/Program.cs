using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Worker;
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
builder.Services.AddDispatchingPersistence();
builder.Services.AddDispatchingServiceBus();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ServiceHealthAdaptiveCardRenderer>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.AddAgentApplicationOptions();
builder.AddAgent<TeamsDispatchAgent>();
builder.Services.AddHostedService<DeliveryIntentOutboxWorker>();
builder.Services.AddHostedService<TeamsDeliveryWorker>();

var app = builder.Build();
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    role = "teams-dispatch-worker",
    timestamp = DateTimeOffset.UtcNow
}));
app.Run();
