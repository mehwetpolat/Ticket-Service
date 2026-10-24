using Microsoft.EntityFrameworkCore;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Dtos;
using TickeTask.Infrastructure.Persistence;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapConfig(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", async (ISessionAuthService auth, ISettingsService settings, AppRuntime runtime) =>
        {
            var ownerId = await auth.GetOwnerIdAsync();
            var providers = await settings.GetProvidersAsync(ownerId);
            return ApiSupport.Json(new ConfigDto(
                runtime.LocalMode,
                !string.IsNullOrWhiteSpace(providers.Jira.BaseUrl) && !string.IsNullOrWhiteSpace(providers.Jira.Email) && !string.IsNullOrWhiteSpace(providers.Jira.Token),
                !string.IsNullOrWhiteSpace(providers.Trello.Key) && !string.IsNullOrWhiteSpace(providers.Trello.Token),
                !string.IsNullOrWhiteSpace(providers.Github.Token),
                !string.IsNullOrWhiteSpace(providers.Asana.Token),
                !string.IsNullOrWhiteSpace(providers.Clickup.Token),
                !string.IsNullOrWhiteSpace(runtime.InboundWebhookToken),
                !string.IsNullOrWhiteSpace(runtime.WhatsAppVerifyToken) && !string.IsNullOrWhiteSpace(runtime.WhatsAppAppSecret),
                runtime.LocalMode));
        });

        app.MapGet("/api/health", async (HttpContext context, AppDbContext database, IEncryptionService encryption, IClock clock) =>
        {
            var checks = new HealthChecksDto(false, encryption.Available);
            try { checks = checks with { Database = await database.Database.CanConnectAsync() }; }
            catch { }
            var healthy = checks.Database && checks.Encryption;
            context.Response.Headers.CacheControl = "no-store";
            return ApiSupport.Json(new HealthDto(healthy ? "ok" : "degraded", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), checks), healthy ? 200 : 503);
        });
    }
}
