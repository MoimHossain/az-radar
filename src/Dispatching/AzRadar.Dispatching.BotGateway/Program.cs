using AzRadar.Dispatching.BotGateway;
using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DispatchingCosmosSettings>(
    builder.Configuration.GetSection(DispatchingCosmosSettings.SectionName));
builder.Services.AddDispatchingPersistence();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.AddAgentApplicationOptions();
builder.AddAgent<AzRadarNotificationAgent>();
builder.Services.AddAzureBotAuthentication(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    role = "teams-bot-gateway",
    timestamp = DateTimeOffset.UtcNow
}));
app.MapPost(
        "/api/messages",
        async (
            HttpRequest request,
            HttpResponse response,
            IAgentHttpAdapter adapter,
            IAgent agent,
            CancellationToken cancellationToken) =>
        {
            await adapter.ProcessAsync(request, response, agent, cancellationToken);
        })
    .RequireAuthorization();

app.Run();

public partial class Program;
