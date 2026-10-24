using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Infrastructure;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapSession(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/session");

        group.MapGet("/", async (ISessionAuthService auth) =>
            ApiSupport.Json(new { user = await auth.GetActorAsync() }));

        group.MapPost("/", async (HttpContext context, SessionAuthService auth, IUserRepository users,
            IPersonRepository people, IPasswordService passwords, ISessionRepository sessions,
            ISecureTokenService tokens, ISettingsService settings, IClock clock, IRateLimiter limiter,
            AppRuntime runtime) =>
        {
            if (!runtime.LocalMode) return ApiSupport.Error(400, "Yerel oturum etkin değil.");
            auth.EnsureOrigin();
            ApiSupport.Limit(context.Request, 10_000, "İstek çok büyük.");
            var input = await ApiSupport.Body<LoginRequest>(context.Request);
            var email = ApiSupport.NormalizeEmail(input.Email);
            var submittedPassword = input.Password ?? string.Empty;
            if (!ApiSupport.ValidEmail(email) || submittedPassword.Length is < 1 or > 1024)
                throw ApiException.BadRequest("E-posta veya şifre hatalı.");

            var address = auth.ClientAddress();
            ApiSupport.RateLimited(limiter, $"login-ip:{address}", 50, 15 * 60);
            ApiSupport.RateLimited(limiter, $"login:{address}:{email}", 10, 15 * 60);

            var isSetup = await users.CountAsync() == 0;
            // Browsers and password managers sometimes include a trailing
            // newline/space with a copied setup secret. Trim only during the
            // one-time setup; normal passwords remain exact.
            var password = isSetup ? submittedPassword.Trim() : submittedPassword;
            if (isSetup && password.Length == 0)
                throw ApiException.BadRequest("Kurulum şifresi boş olamaz.");

            if (isSetup)
            {
                if (!await passwords.ValidSetupPasswordAsync(password))
                {
                    await passwords.VerifyDummyAsync(password);
                    return ApiSupport.Error(401, "Kurulum şifresi hatalı. Şifre data/secrets/admin-password dosyasında; değeri boşluk bırakmadan girin.");
                }
                if (password.Length < 12) return ApiSupport.Error(400, "Yönetici şifresi en az 12 karakter olmalı.");
                var passwordRecord = await passwords.CreateAsync(password);
                var adminId = Guid.NewGuid().ToString("N");
                adminId = $"usr_{adminId}";
                var timestamp = clock.UnixNow;
                await users.AddAsync(new User
                {
                    Id = adminId,
                    Email = email,
                    Name = "Yönetici",
                    PasswordSalt = passwordRecord.Salt,
                    PasswordHash = passwordRecord.Hash,
                    Role = UserRole.Admin,
                    Status = UserStatus.Active,
                    CreatedAt = timestamp,
                    ApprovedAt = timestamp
                });
                await people.AddAsync(new Person
                {
                    Id = $"per_{Guid.NewGuid():N}",
                    Name = "Yönetici",
                    Email = email,
                    UserId = adminId,
                    CreatedAt = timestamp,
                    UpdatedAt = timestamp
                });
                await settings.SetAsync("local-user", "admin_password", new { salt = passwordRecord.Salt, hash = passwordRecord.Hash });
                await settings.SetAsync("local-user", "admin_initialized", new { at = timestamp, email });
            }

            var user = await users.GetByEmailAsync(email);
            if (user is null)
            {
                await passwords.VerifyDummyAsync(password);
                return ApiSupport.Error(401, "E-posta veya şifre hatalı.");
            }
            if (!await passwords.VerifyAsync(password, user.PasswordSalt, user.PasswordHash))
                return ApiSupport.Error(401, "E-posta veya şifre hatalı.");
            if (user.Status != UserStatus.Active)
                return ApiSupport.Error(403, user.Status == UserStatus.Pending ? "Hesabınız yönetici onayı bekliyor." : "Hesap devre dışı.");

            var token = tokens.CreateToken();
            var now = clock.UnixNow;
            await sessions.DeleteExpiredAsync(now);
            await sessions.AddAsync(new Session
            {
                Id = $"ses_{Guid.NewGuid():N}",
                UserId = user.Id,
                TokenHash = tokens.Sha256(token),
                ExpiresAt = now + SessionConstants.SessionHours * 60 * 60,
                CreatedAt = now
            });
            await sessions.TrimAsync(user.Id, 10);

            context.Response.Cookies.Append(SessionConstants.CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = auth.SecureCookie,
                Path = "/",
                MaxAge = TimeSpan.FromHours(SessionConstants.SessionHours)
            });
            return ApiSupport.Json(new
            {
                ok = true,
                user = new SessionUserDto(user.Id, user.Name, user.Email, user.Role)
            });
        });

        group.MapPatch("/", async (HttpContext context, ISessionAuthService authService,
            SessionAuthService auth, IUserRepository users, IPasswordService passwords,
            ISessionRepository sessions, ISecureTokenService tokens, ISettingsService settings,
            IClock clock, IRateLimiter limiter, IUnitOfWork unitOfWork) =>
        {
            var actor = await authService.GetActorAsync();
            ApiSupport.Limit(context.Request, 10_000, "İstek çok büyük.");
            ApiSupport.RateLimited(limiter, $"password:{actor.Id}:{auth.ClientAddress()}", 5, 15 * 60);
            var input = await ApiSupport.Body<ChangePasswordRequest>(context.Request);
            if (input.CurrentPassword.Length > 1024 || input.NewPassword.Length is < 12 or > 128)
                throw ApiException.BadRequest("Yeni şifre 12-128 karakter olmalı.");

            var user = await users.GetByIdAsync(actor.Id) ?? throw ApiException.Unauthorized();
            if (!await passwords.VerifyAsync(input.CurrentPassword, user.PasswordSalt, user.PasswordHash))
                return ApiSupport.Error(400, "Mevcut şifre hatalı.");

            var password = await passwords.CreateAsync(input.NewPassword);
            user.PasswordSalt = password.Salt;
            user.PasswordHash = password.Hash;
            await unitOfWork.SaveChangesAsync();
            if (user.Role == UserRole.Admin)
            {
                await settings.SetAsync("local-user", "admin_password", new { salt = password.Salt, hash = password.Hash });
                await settings.SetAsync("local-user", "admin_password_changed_at", new { at = clock.UnixNow });
            }

            var cookie = context.Request.Cookies[SessionConstants.CookieName];
            var hash = cookie is null ? string.Empty : tokens.Sha256(cookie);
            await sessions.DeleteByUserExceptTokenAsync(user.Id, hash);
            return ApiSupport.Json(new { ok = true });
        });

        group.MapDelete("/", async (HttpContext context, ISessionRepository sessions, ISecureTokenService tokens, SessionAuthService auth) =>
        {
            var cookie = context.Request.Cookies[SessionConstants.CookieName];
            if (cookie is not null) await sessions.DeleteByTokenHashAsync(tokens.Sha256(cookie));
            context.Response.Cookies.Append(SessionConstants.CookieName, string.Empty, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = auth.SecureCookie,
                Path = "/",
                MaxAge = TimeSpan.Zero
            });
            return ApiSupport.Json(new { ok = true });
        });
    }
}
