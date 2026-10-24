using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapCustomers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customers");

        group.MapGet("/", async (ISessionAuthService auth, ICustomerRepository customers) =>
        {
            var actor = await auth.GetActorAsync();
            if (actor.Role != Domain.Enums.UserRole.Admin) return ApiSupport.Json(new { customers = Array.Empty<object>() });
            var ownerId = await auth.GetOwnerIdAsync();
            var rows = await customers.ListAsync(ownerId);
            return ApiSupport.Json(new
            {
                customers = rows.Select(customer => new
                {
                    id = customer.Id,
                    name = customer.Name,
                    email = customer.Email,
                    phone = customer.Phone,
                    target = customer.Target,
                    destinationId = customer.DestinationId,
                    createdAt = customer.CreatedAt
                })
            });
        });

        group.MapPost("/", async (ISessionAuthService auth, ICustomerRepository customers, ITicketTargetCatalog catalog,
            IClock clock, IUnitOfWork unitOfWork, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var input = await ApiSupport.Body<SaveCustomerRequest>(request);
            var name = Guard.Text(input.Name, 2, 120, "Kayıt adı 2-120 karakter olmalı.");
            var email = (input.Email ?? string.Empty).Trim();
            if (email.Length > 0 && !ApiSupport.ValidEmail(email)) throw ApiException.BadRequest("Geçerli bir e-posta adresi girin.");
            var phone = (input.Phone ?? string.Empty).Trim();
            if (phone.Length > 40) throw ApiException.BadRequest("Telefon en fazla 40 karakter olabilir.");
            var destination = (input.DestinationId ?? string.Empty).Trim();
            if (destination.Length is < 1 or > 200) throw ApiException.BadRequest("Hedef kimliği gerekli.");
            destination = catalog.NormalizeDestination(input.Target, destination);
            var error = catalog.DestinationError(input.Target, destination);
            if (error is not null) throw ApiException.BadRequest(error);

            var duplicate = await customers.FindByNameAsync(ownerId, name);
            if (duplicate is not null && !string.Equals(duplicate.Id, input.Id, StringComparison.Ordinal))
                throw ApiException.Conflict("Bu adda bir kayıtlı taraf zaten var.");

            if (!string.IsNullOrWhiteSpace(input.Id))
            {
                var customer = await customers.GetAsync(input.Id, ownerId) ?? throw ApiException.NotFound("Kayıtlı taraf bulunamadı.");
                customer.Name = name;
                customer.Email = email.Length == 0 ? null : email;
                customer.Phone = phone.Length == 0 ? null : phone;
                customer.Target = input.Target;
                customer.DestinationId = destination;
                customer.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync();
                return ApiSupport.Json(new { id = customer.Id });
            }

            var id = $"cus_{Guid.NewGuid():N}";
            var now = clock.UnixNow;
            await customers.AddAsync(new Customer
            {
                Id = id,
                OwnerId = ownerId,
                Name = name,
                Email = email.Length == 0 ? null : email,
                Phone = phone.Length == 0 ? null : phone,
                Target = input.Target,
                DestinationId = destination,
                CreatedAt = now,
                UpdatedAt = now
            });
            return ApiSupport.Json(new { id }, 201);
        });

        group.MapDelete("/", async (ISessionAuthService auth, ICustomerRepository customers, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var id = request.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(id)) throw ApiException.BadRequest("Kayıt seçilmedi.");
            var customer = await customers.GetAsync(id, ownerId) ?? throw ApiException.NotFound("Kayıtlı taraf bulunamadı.");
            if (await customers.CountRelatedAsync(id, ownerId) > 0)
                return ApiSupport.Error(409, "Bu kayda bağlı mesaj veya ticket var. Kayıt korunuyor.");
            await customers.RemoveAsync(customer);
            return ApiSupport.Json(new { ok = true });
        });
    }
}
