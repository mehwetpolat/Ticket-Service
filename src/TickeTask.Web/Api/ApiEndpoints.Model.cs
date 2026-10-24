using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Web.Api;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapModel(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/local-llm", async (ISessionAuthService auth, ILocalLlmClient llm) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            return ApiSupport.Json(await llm.CheckAsync(ownerId));
        });

        app.MapPost("/api/local-llm", async (ISessionAuthService auth) =>
        {
            await auth.RequireAdminAsync();
            return ApiSupport.Error(409, "Model indirme yöntemi yenilendi. Sayfayı yenileyip Ayarlar bölümündeki düğmeyi tekrar kullanın.");
        });

        app.MapGet("/api/model-storage", async (HttpContext context, ISessionAuthService auth, IModelControlClient control) =>
        {
            await auth.RequireAdminAsync();
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Query["view"] == "folders")
            {
                var slot = context.Request.Query["slot"].ToString();
                if (slot is not ("primary" or "secondary")) throw ApiException.BadRequest("Depolama konumu geçersiz.");
                var folder = context.Request.Query["folder"].ToString();
                if (folder.Length > 240) throw ApiException.BadRequest("Klasör adı geçersiz.");
                return ApiSupport.Json(await control.GetFoldersAsync(slot, folder));
            }
            return ApiSupport.Json(await control.GetStatusAsync());
        });

        app.MapPost("/api/model-storage/pick-folder", async (ISessionAuthService authService, SessionAuthService auth, IModelFolderPicker picker, IRateLimiter limiter, HttpRequest request) =>
        {
            await authService.RequireAdminAsync();
            ApiSupport.RateLimited(limiter, $"model-picker:{auth.ClientAddress()}", 6, 60);
            var input = await ApiSupport.Body<ModelFolderPickerRequest>(request);
            if ((input.Path ?? string.Empty).Length > 500) throw ApiException.BadRequest("Klasör yolu geçersiz.");
            var path = await picker.PickAsync(input.Path);
            return ApiSupport.Json(new { path });
        });

        app.MapPost("/api/model-storage", async (ISessionAuthService auth, IModelControlClient control, HttpRequest request) =>        {
            await auth.RequireAdminAsync();
            var input = await ApiSupport.Body<ModelStorageRequest>(request);
            switch ((input.Action ?? string.Empty).ToLowerInvariant())
            {
                case "pull":
                    if ((input.Model ?? string.Empty).Length is < 2 or > 100) throw ApiException.BadRequest("Model adı geçersiz.");
                    await control.PullAsync(input.Model!);
                    return ApiSupport.Json(new { ok = true }, 202);
                case "cancel":
                    await control.CancelAsync();
                    return ApiSupport.Json(new { ok = true });
                case "select":
                    {
                        if (input.Slot is not ("primary" or "secondary")) throw ApiException.BadRequest("Depolama konumu geçersiz.");
                        if ((input.Folder ?? string.Empty).Length > 240) throw ApiException.BadRequest("Klasör adı geçersiz.");
                        var selection = await control.SelectAsync(input.Slot, input.Folder ?? string.Empty);
                        return ApiSupport.Json(new { selection });
                    }
                case "path":
                    {
                        if ((input.Path ?? string.Empty).Length is < 1 or > 500) throw ApiException.BadRequest("Klasör yolu geçersiz.");
                        var selection = await control.SelectPathAsync(input.Path!);
                        return ApiSupport.Json(new { selection });
                    }
                case "folder":
                    {
                        if (input.Slot is not ("primary" or "secondary")) throw ApiException.BadRequest("Depolama konumu geçersiz.");
                        var name = (input.Name ?? string.Empty).Trim();
                        if (name.Length is < 1 or > 60) throw ApiException.BadRequest("Klasör adı geçersiz.");
                        return ApiSupport.Json(await control.CreateFolderAsync(input.Slot, input.Folder ?? string.Empty, name));
                    }
                default:
                    throw ApiException.BadRequest("Geçersiz model işlemi.");
            }
        });
    }
}
