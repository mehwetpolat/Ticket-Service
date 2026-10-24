using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;

namespace TickeTask.Web.Security;

public static class SessionConstants
{
    public const string CookieName = "ticketask_session";
    public const int SessionHours = 12;
}

public sealed class SessionAuthService(
    IHttpContextAccessor accessor,
    ISessionRepository sessions,
    ISecureTokenService tokens,
    IClock clock,
    AppRuntime runtime) : ISessionAuthService
{
    private HttpContext Context => accessor.HttpContext ?? throw ApiException.Unauthorized();

    public async Task<SessionUserDto> GetActorAsync(CancellationToken ct = default)
    {
        if (!runtime.LocalMode) throw ApiException.Unauthorized();
        EnsureOrigin();
        var token = Context.Request.Cookies[SessionConstants.CookieName];
        if (!tokens.ValidSessionToken(token)) throw ApiException.Unauthorized();
        var actor = await sessions.GetActorAsync(tokens.Sha256(token!), clock.UnixNow, ct);
        if (actor is null) throw ApiException.Unauthorized();
        return new SessionUserDto(actor.Id, actor.Name, actor.Email, actor.Role);
    }

    public async Task<string> GetOwnerIdAsync(CancellationToken ct = default)
    {
        await GetActorAsync(ct);
        return "local-user";
    }

    public async Task<SessionUserDto> RequireAdminAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor.Role != Domain.Enums.UserRole.Admin) throw ApiException.Forbidden();
        return actor;
    }

    public void EnsureOrigin()
    {
        var request = Context.Request;
        if (request.Headers["Sec-Fetch-Site"] == "cross-site") throw ApiException.Unauthorized();
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0) return;
        if (origin == "null") throw ApiException.Unauthorized();
        var requestOrigin = $"{request.Scheme}://{request.Host}";
        var configured = ConfiguredOrigin();
        if (!string.Equals(origin, requestOrigin, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(origin, configured, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Unauthorized();
    }

    public bool SecureCookie
    {
        get
        {
            var request = Context.Request;
            if (ConfiguredOrigin()?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true) return true;
            if (request.IsHttps) return true;
            return runtime.TrustProxy && request.Headers["X-Forwarded-Proto"] == "https";
        }
    }

    public string? ConfiguredOrigin()
    {
        if (string.IsNullOrWhiteSpace(runtime.PublicOrigin)) return null;
        return Uri.TryCreate(runtime.PublicOrigin, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
    }

    public string ClientAddress()
    {
        var request = Context.Request;
        if (runtime.TrustProxy)
        {
            var direct = request.Headers["CF-Connecting-IP"].ToString();
            if (direct.Length > 0) return direct;
            var forwarded = request.Headers["X-Forwarded-For"].ToString();
            if (forwarded.Length > 0) return forwarded.Split(',')[0].Trim();
        }
        return Context.Connection.RemoteIpAddress?.ToString() ?? "local";
    }
}
