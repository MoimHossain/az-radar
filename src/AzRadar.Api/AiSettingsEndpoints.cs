using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.AspNetCore.Mvc;

namespace AzRadar.Api;

public static class AiSettingsEndpoints
{
    public static void MapAiSettings(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai-settings")
            .RequireRateLimiting("ai-management")
            .WithMetadata(new RequestSizeLimitAttribute(16_384));
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception ex)
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AiSettings");
                logger.LogWarning("AI settings operation failed: {ErrorType}", ex.GetType().Name);
                var error = ex as AiProviderException;
                var status = error?.Code switch
                {
                    "invalid_settings" or "pat_required" => 400,
                    "configuration_changed" => 412,
                    "inference_failed" or "empty_response" => 502,
                    _ => 503
                };
                return Results.Json(new
                {
                    error = error?.Message ?? "AI settings are unavailable. Check Key Vault, storage, and network access.",
                    code = error?.Code ?? "settings_unavailable"
                }, statusCode: status);
            }
        });
        group.MapGet("/", async (AiSettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetAsync(ct)));
        group.MapPost("/test", async (AiSettingsRequest request, IAiCompletionService completion, CancellationToken ct) =>
            Results.Ok(await completion.TestAsync(request, ct)));
        group.MapPut("/", async (AiSettingsRequest request, AiSettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.SaveAsync(request, ct)));
    }
}
