using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static readonly HashSet<string> DeviceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop", "laptop", "tablet", "printer", "network", "server", "other"
    };

    private static void MapDevices(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices");

        group.MapGet("/", async (ISessionAuthService auth, IDeviceRepository devices) =>
        {
            var actor = await auth.GetActorAsync();
            var rows = await devices.ListAsync(actor.Role == UserRole.Admin, actor.Id);
            return ApiSupport.Json(new
            {
                devices = rows.Select(device => new DeviceDto(
                    device.Id, device.Name, device.Kind, device.IpAddress, device.Serial, device.Location,
                    device.PersonId, device.Person?.Name, device.ParentId, device.Notes))
            });
        });

        group.MapPost("/", async (ISessionAuthService auth, IDeviceRepository devices, IPersonRepository people,
            IClock clock, IUnitOfWork unitOfWork, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var input = await ApiSupport.Body<SaveDeviceRequest>(request);
            var name = Guard.Text(input.Name, 2, 120, "Cihaz adı 2-120 karakter olmalı.");
            if (!DeviceKinds.Contains(input.Kind)) throw ApiException.BadRequest("Cihaz türü geçersiz.");
            var kind = input.Kind.ToLowerInvariant();
            var ip = Guard.OptionalText(input.IpAddress, 45, "IP adresi en fazla 45 karakter olabilir.");
            var serial = Guard.OptionalText(input.Serial, 100, "Seri numarası en fazla 100 karakter olabilir.");
            var location = Guard.OptionalText(input.Location, 120, "Konum en fazla 120 karakter olabilir.");
            var notes = Guard.OptionalText(input.Notes, 1000, "Notlar en fazla 1000 karakter olabilir.");

            if (!string.IsNullOrWhiteSpace(input.PersonId) && await people.GetByIdAsync(input.PersonId) is null)
                throw ApiException.BadRequest("Çalışan bulunamadı.");
            if (!string.IsNullOrWhiteSpace(input.ParentId) && await devices.GetByIdAsync(input.ParentId) is null)
                throw ApiException.BadRequest("Bağlanacak cihaz bulunamadı.");
            if (!string.IsNullOrWhiteSpace(input.Id) && input.Id == input.ParentId)
                throw ApiException.BadRequest("Cihaz kendisine bağlanamaz.");

            if (!string.IsNullOrWhiteSpace(input.Id))
            {
                var device = await devices.GetByIdAsync(input.Id) ?? throw ApiException.NotFound("Cihaz bulunamadı.");
                var parent = input.ParentId;
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    if (parent == input.Id) throw ApiException.BadRequest("Ağ bağlantısında döngü oluşamaz.");
                    parent = await devices.GetParentIdAsync(parent);
                }
                device.Name = name;
                device.Kind = kind;
                device.IpAddress = ip;
                device.Serial = serial;
                device.Location = location;
                device.PersonId = string.IsNullOrWhiteSpace(input.PersonId) ? null : input.PersonId;
                device.ParentId = string.IsNullOrWhiteSpace(input.ParentId) ? null : input.ParentId;
                device.Notes = notes;
                device.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync();
                return ApiSupport.Json(new { id = device.Id });
            }

            var id = $"dev_{Guid.NewGuid():N}";
            var now = clock.UnixNow;
            await devices.AddAsync(new Device
            {
                Id = id,
                Name = name,
                Kind = kind,
                IpAddress = ip,
                Serial = serial,
                Location = location,
                PersonId = string.IsNullOrWhiteSpace(input.PersonId) ? null : input.PersonId,
                ParentId = string.IsNullOrWhiteSpace(input.ParentId) ? null : input.ParentId,
                Notes = notes,
                CreatedAt = now,
                UpdatedAt = now
            });
            return ApiSupport.Json(new { id }, 201);
        });
    }
}
