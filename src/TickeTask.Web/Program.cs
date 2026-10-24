using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Infrastructure;
using TickeTask.Infrastructure.Persistence;
using TickeTask.Web.Api;
using TickeTask.Web.Security;

var builder = WebApplication.CreateBuilder(args);
var runtime = RuntimeBootstrap.Create(builder.Environment.ContentRootPath);
var databasePath = RuntimeBootstrap.ResolveDatabasePath(builder.Environment.ContentRootPath, builder.Configuration["Database:Path"]);

builder.Services.AddHttpContextAccessor();
builder.Services.AddTickeTaskInfrastructure(runtime, databasePath);
builder.Services.AddScoped<SessionAuthService>();
builder.Services.AddScoped<ISessionAuthService>(services => services.GetRequiredService<SessionAuthService>());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.MigrateAsync();
}

if (runtime.TrustProxy) app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        if (context.Request.IsHttps) headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        return Task.CompletedTask;
    });
    await next();
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException exception)
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = exception.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        if (exception.Data["retryAfter"] is int retryAfter) context.Response.Headers.RetryAfter = retryAfter.ToString();
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = exception.Message }, JsonDefaults.Options));
    }
    catch (BadHttpRequestException)
    {
        if (!context.Response.HasStarted) await WriteError(context, 400, "İstek biçimi geçersiz.");
    }
    catch (JsonException)
    {
        if (!context.Response.HasStarted) await WriteError(context, 400, "İstek biçimi geçersiz.");
    }
    catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(exception, "Unhandled request failure for {Method} {Path}", context.Request.Method, context.Request.Path);
        if (!context.Response.HasStarted) await WriteError(context, 500, "Beklenmeyen bir hata oluştu.");
    }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var requestPath = context.Context.Request.Path;
        if (requestPath.StartsWithSegments("/assets") ||
            requestPath.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
        }
    }
});
app.MapTickeTaskApi();
app.MapFallback("/api/{**rest}", () => ApiSupport.Error(404, "Bulunamadı."));
app.MapFallbackToFile("index.html");

app.Run();

static async Task WriteError(HttpContext context, int status, string message)
{
    context.Response.Clear();
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, JsonDefaults.Options));
}

public partial class Program;
