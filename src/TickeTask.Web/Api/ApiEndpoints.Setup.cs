using TickeTask.Application.Abstractions;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapSetup(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/setup-status", async (HttpContext context, ISessionAuthService auth, ISetupStatusService setup) =>
        {
            await auth.RequireAdminAsync();
            context.Response.Headers.CacheControl = "no-store";
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            return ApiSupport.Json(await setup.GetAsync(origin));
        });
    }
}
