using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapUsers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapGet("/", async (ISessionAuthService auth, IUserRepository users) =>
        {
            await auth.RequireAdminAsync();
            return ApiSupport.Json(new { users = await users.ListAsync() });
        });

        group.MapPatch("/", async (ISessionAuthService auth, IUserRepository users, IPersonRepository people,
            ISessionRepository sessions, IClock clock, IUnitOfWork unitOfWork, HttpRequest request) =>
        {
            var admin = await auth.RequireAdminAsync();
            var input = await ApiSupport.Body<UpdateUserRequest>(request);
            if (input.Status is not (UserStatus.Active or UserStatus.Disabled))
                throw ApiException.BadRequest("Geçersiz kullanıcı durumu.");
            if (input.Id == admin.Id && input.Status == UserStatus.Disabled)
                return ApiSupport.Error(400, "Kendi hesabınızı devre dışı bırakamazsınız.");

            var user = await users.GetByIdAsync(input.Id) ?? throw ApiException.NotFound("Kullanıcı bulunamadı.");
            user.Status = input.Status;
            if (input.Status == UserStatus.Active && user.ApprovedAt is null) user.ApprovedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync();
            if (input.Status == UserStatus.Disabled) await sessions.DeleteByUserIdAsync(user.Id);
            if (input.Status == UserStatus.Active)
            {
                var person = await people.GetByUserIdAsync(user.Id);
                if (person is null)
                {
                    var now = clock.UnixNow;
                    await people.AddAsync(new Person
                    {
                        Id = $"per_{Guid.NewGuid():N}",
                        Name = user.Name,
                        Email = user.Email,
                        UserId = user.Id,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
            return ApiSupport.Json(new { ok = true });
        });

        group.MapPost("/register", async (HttpContext context, SessionAuthService auth, IUserRepository users,
            IPasswordService passwords, IClock clock, IRateLimiter limiter, AppRuntime runtime,
            ISecureTokenService tokens) =>
        {
            if (!runtime.LocalMode) return ApiSupport.Error(400, "Kayıt yalnızca kurum içi kurulumda açık.");
            if (!runtime.AllowSelfRegistration)
                return ApiSupport.Error(403, "Kendi kendine kayıt yönetici tarafından kapatıldı. IT biriminden hesap talep edin.");
            auth.EnsureOrigin();
            ApiSupport.Limit(context.Request, 10_000, "İstek çok büyük.");
            ApiSupport.RateLimited(limiter, $"register:{auth.ClientAddress()}", 5, 60 * 60);

            var input = await ApiSupport.Body<RegisterRequest>(context.Request);
            var name = Guard.Text(input.Name, 2, 120, "Ad soyad 2-120 karakter olmalı.");
            var email = ApiSupport.NormalizeEmail(input.Email);
            if (!ApiSupport.ValidEmail(email)) throw ApiException.BadRequest("Geçerli bir e-posta adresi girin.");
            if (input.Password.Length is < 12 or > 128) throw ApiException.BadRequest("Şifre 12-128 karakter olmalı.");

            if (await users.CountActiveAdminsAsync() == 0)
                return ApiSupport.Error(409, "Önce yönetici ilk girişini tamamlamalı.");
            if (await users.GetByEmailAsync(email) is not null)
                return ApiSupport.Error(409, "Bu e-posta ile bir hesap zaten var. Giriş yapmayı deneyin.");

            var password = await passwords.CreateAsync(input.Password);
            await users.AddAsync(new User
            {
                Id = $"usr_{Guid.NewGuid():N}",
                Email = email,
                Name = name,
                PasswordSalt = password.Salt,
                PasswordHash = password.Hash,
                Role = UserRole.Member,
                Status = UserStatus.Pending,
                CreatedAt = clock.UnixNow
            });
            return ApiSupport.Json(new { ok = true, message = "Kayıt alındı. Yönetici onayı sonrası giriş yapabilirsiniz." }, 201);
        });
    }
}
