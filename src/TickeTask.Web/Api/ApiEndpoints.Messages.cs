using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapMessages(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/messages");

        group.MapGet("/", async (ISessionAuthService auth, IMessageRepository messages) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var rows = await messages.ListAsync(ownerId, actor.Role == UserRole.Admin, actor.Id);
            return ApiSupport.Json(new
            {
                messages = rows.Select(message => new MessageDto(
                    message.Id, message.CustomerId, message.CustomerName, message.PersonId, message.PersonName,
                    message.DeviceId, message.DeviceName, message.CreatedBy, message.CreatedByName, message.Channel,
                    message.Sender, message.Subject, message.Content, message.Status, message.ReceivedAt))
            });
        });

        group.MapPost("/", async (ISessionAuthService auth, IMessageRepository messages, IPersonRepository people,
            IDeviceRepository devices, IClock clock, HttpRequest request) =>
        {
            var actor = await auth.GetActorAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var input = await ApiSupport.Body<SaveMessageRequest>(request);
            if (actor.Role != UserRole.Admin && input.CustomerId is not null)
                return ApiSupport.Error(403, "Harici hedef seçme yetkiniz yok.");
            var content = Guard.Text(input.Content, 5, 12_000, "Mesaj en az 5, en fazla 12000 karakter olmalı.");
            var sender = Guard.OptionalText(input.Sender, 160, "Gönderen en fazla 160 karakter olabilir.");
            var subject = Guard.OptionalText(input.Subject, 200, "Konu en fazla 200 karakter olabilir.");
            var externalId = Guard.OptionalText(input.ExternalId, 200, "Dış kimlik en fazla 200 karakter olabilir.");

            var personId = input.PersonId;
            if (actor.Role == UserRole.Member)
            {
                var own = await people.GetByUserIdAsync(actor.Id);
                personId = own?.Id;
            }
            else if (!string.IsNullOrWhiteSpace(personId) && await people.GetByIdAsync(personId) is null)
            {
                return ApiSupport.Error(400, "Çalışan bulunamadı.");
            }
            if (!string.IsNullOrWhiteSpace(input.DeviceId))
            {
                var device = await devices.GetByIdAsync(input.DeviceId);
                if (device is null || device.PersonId != personId) return ApiSupport.Error(400, "Cihaz seçimi geçersiz.");
            }

            var id = $"msg_{Guid.NewGuid():N}";
            await messages.AddAsync(new Message
            {
                Id = id,
                OwnerId = ownerId,
                CustomerId = input.CustomerId,
                PersonId = personId,
                DeviceId = string.IsNullOrWhiteSpace(input.DeviceId) ? null : input.DeviceId,
                CreatedBy = actor.Id,
                Channel = input.Channel,
                Sender = sender,
                Subject = subject,
                Content = content,
                ExternalId = externalId,
                Status = MessageStatus.New,
                ReceivedAt = clock.UnixNow
            });
            return ApiSupport.Json(new { id }, 201);
        });

        group.MapPatch("/", async (ISessionAuthService auth, IMessageRepository messages, IUnitOfWork unitOfWork,
            HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var input = await ApiSupport.Body<UpdateMessageStatusRequest>(request);
            if (!Enum.IsDefined(input.Status)) throw ApiException.BadRequest("Geçersiz mesaj durumu.");
            var message = await messages.GetAsync(input.Id, ownerId);
            if (message is not null)
            {
                message.Status = input.Status;
                await unitOfWork.SaveChangesAsync();
            }
            return ApiSupport.Json(new { ok = true });
        });
    }
}
