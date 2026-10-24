using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Models;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapProject(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project");

        group.MapGet("/", async (ISessionAuthService auth, IProjectService project) =>
        {
            var ownerId = await auth.GetOwnerIdAsync();
            return ApiSupport.Json(new { project = await project.GetAsync(ownerId) });
        });

        group.MapPost("/", async (ISessionAuthService auth, IProjectService project, ITicketTargetCatalog catalog, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var input = await ApiSupport.Body<SaveProjectRequest>(request);
            var destination = (input.DestinationId ?? string.Empty).Trim();
            if (destination.Length is < 1 or > 200) throw ApiException.BadRequest("Hedef kimliği gerekli.");
            destination = catalog.NormalizeDestination(input.Target, destination);
            var error = catalog.DestinationError(input.Target, destination);
            if (error is not null) throw ApiException.BadRequest(error);
            var name = Guard.Text(input.Name, 2, 100, "Proje adı 2-100 karakter olmalı.");
            await project.SaveAsync(ownerId, new ProjectConfig { Target = input.Target, DestinationId = destination, Name = name });
            return ApiSupport.Json(new { ok = true, target = input.Target, destinationLabel = catalog.Get(input.Target).DestinationLabel });
        });
    }
}
