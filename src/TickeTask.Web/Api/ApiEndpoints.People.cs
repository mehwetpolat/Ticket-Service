using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapPeople(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/people");

        group.MapGet("/", async (ISessionAuthService auth, IPersonRepository people) =>
        {
            var actor = await auth.GetActorAsync();
            var rows = await people.ListAsync(actor.Role == UserRole.Admin, actor.Id);
            return ApiSupport.Json(new
            {
                people = rows.Select(person => new PersonDto(
                    person.Id, person.Name, person.Email, person.Department, person.Phone, person.UserId))
            });
        });

        group.MapPost("/", async (ISessionAuthService auth, IPersonRepository people, IUserRepository users,
            IClock clock, IUnitOfWork unitOfWork, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var input = await ApiSupport.Body<SavePersonRequest>(request);
            var name = Guard.Text(input.Name, 2, 120, "Ad soyad 2-120 karakter olmalı.");
            var email = (input.Email ?? string.Empty).Trim();
            if (email.Length > 0 && !ApiSupport.ValidEmail(email)) throw ApiException.BadRequest("Geçerli bir e-posta adresi girin.");
            var department = Guard.OptionalText(input.Department, 100, "Departman en fazla 100 karakter olabilir.");
            var phone = Guard.OptionalText(input.Phone, 40, "Telefon en fazla 40 karakter olabilir.");
            if (!string.IsNullOrWhiteSpace(input.UserId))
            {
                var user = await users.GetByIdAsync(input.UserId);
                if (user is null || user.Status != UserStatus.Active) throw ApiException.BadRequest("Aktif kullanıcı bulunamadı.");
                var linked = await people.GetByUserIdAsync(input.UserId);
                if (linked is not null && !string.Equals(linked.Id, input.Id, StringComparison.Ordinal))
                    throw ApiException.Conflict("Bu kullanıcı başka bir çalışana bağlı.");
            }

            if (!string.IsNullOrWhiteSpace(input.Id))
            {
                var person = await people.GetByIdAsync(input.Id) ?? throw ApiException.NotFound("Çalışan bulunamadı.");
                person.Name = name;
                person.Email = email.Length == 0 ? null : email;
                person.Department = department;
                person.Phone = phone;
                person.UserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId;
                person.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync();
                return ApiSupport.Json(new { id = person.Id });
            }

            var id = $"per_{Guid.NewGuid():N}";
            var now = clock.UnixNow;
            await people.AddAsync(new Person
            {
                Id = id,
                Name = name,
                Email = email.Length == 0 ? null : email,
                Department = department,
                Phone = phone,
                UserId = string.IsNullOrWhiteSpace(input.UserId) ? null : input.UserId,
                CreatedAt = now,
                UpdatedAt = now
            });
            return ApiSupport.Json(new { id }, 201);
        });
    }
}
