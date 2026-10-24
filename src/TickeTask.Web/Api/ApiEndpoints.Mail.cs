using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Models;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapMail(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/mail-config", async (HttpContext context, ISessionAuthService auth, IMailConfigService mail,
            AppRuntime runtime, ISecureTokenService tokens) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (MailServiceAuthorized(context.Request, runtime, tokens))
                return ApiSupport.Json(new { config = await mail.GetAsync() });
            await auth.RequireAdminAsync();
            return ApiSupport.Json(new { config = await mail.GetForAdminAsync(), status = await mail.GetPollStatusAsync() });
        });

        app.MapPost("/api/mail-config", async (HttpContext context, ISessionAuthService auth, IMailConfigService mail,
            AppRuntime runtime, ISecureTokenService tokens) =>
        {
            ApiSupport.Limit(context.Request, 50_000, "İstek çok büyük.");
            if (MailServiceAuthorized(context.Request, runtime, tokens))
            {
                var input = await ApiSupport.Body<MailPollStatusRequest>(context.Request);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var status = new MailPollStatus
                {
                    State = ParseCheckState(input.State),
                    Message = (input.Message ?? string.Empty).Trim(),
                    CheckedAt = input.CheckedAt ?? now
                };
                if (status.Message.Length > 500) throw ApiException.BadRequest("Geçersiz poller durumu.");
                if (input.Mailboxes is not null)
                {
                    status.Mailboxes = new Dictionary<string, MailboxPollStatus>();
                    foreach (var (key, value) in input.Mailboxes)
                    {
                        var message = (value.Message ?? string.Empty).Trim();
                        if (message.Length > 500) throw ApiException.BadRequest("Geçersiz poller durumu.");
                        status.Mailboxes[key] = new MailboxPollStatus
                        {
                            State = ParseCheckState(value.State),
                            Message = message,
                            CheckedAt = value.CheckedAt ?? now,
                            LastUid = value.LastUid
                        };
                    }
                }
                await mail.SetPollStatusAsync(status);
                return ApiSupport.Json(new { ok = true });
            }

            await auth.RequireAdminAsync();
            var config = await ApiSupport.Body<MailOptions>(context.Request);
            await mail.SaveAsync(config);
            return ApiSupport.Json(new { ok = true });
        });

        app.MapGet("/api/mail-senders", async (ISessionAuthService auth, IMailSenderService senders) =>
        {
            await auth.RequireAdminAsync();
            return ApiSupport.Json(new { senders = await senders.ListAsync() });
        });

        app.MapPost("/api/mail-senders", async (HttpContext context, ISessionAuthService auth, IMailSenderService senders) =>
        {
            await auth.RequireAdminAsync();
            ApiSupport.Limit(context.Request, 10_000, "İstek çok büyük.");
            var input = await ApiSupport.Body<SaveMailSenderRequest>(context.Request);
            var email = ApiSupport.NormalizeEmail(input.Email);
            if (!ApiSupport.ValidEmail(email)) throw ApiException.BadRequest("Geçerli bir e-posta adresi girin.");
            var name = Guard.Text(input.Name, 2, 120, "Gönderen adı 2-120 karakter olmalı.");
            await senders.SaveAsync(email, name, input.Active);
            return ApiSupport.Json(new { ok = true });
        });

        app.MapGet("/api/mail-domains", async (ISessionAuthService auth, IMailSenderService senders) =>
        {
            await auth.RequireAdminAsync();
            return ApiSupport.Json(new { domains = await senders.ListDomainsAsync() });
        });

        app.MapPost("/api/mail-domains", async (HttpContext context, ISessionAuthService auth, IMailSenderService senders) =>
        {
            await auth.RequireAdminAsync();
            ApiSupport.Limit(context.Request, 10_000, "İstek çok büyük.");
            var input = await ApiSupport.Body<SaveMailDomainRequest>(context.Request);
            var domain = (input.Domain ?? string.Empty).Trim();
            if (domain.Length is < 3 or > 253) throw ApiException.BadRequest("Geçerli bir alan adı girin. Örnek: sirket.com");
            await senders.AddDomainAsync(domain);
            return ApiSupport.Json(new { ok = true, domains = await senders.ListDomainsAsync() });
        });

        app.MapDelete("/api/mail-domains", async (ISessionAuthService auth, IMailSenderService senders, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            await senders.RemoveDomainAsync(request.Query["domain"].ToString());
            return ApiSupport.Json(new { ok = true, domains = await senders.ListDomainsAsync() });
        });

        app.MapGet("/api/mail-ingestions", async (HttpContext context, ISessionAuthService auth,
            Application.Abstractions.Persistence.IMailIngestionRepository ingestions, IClock clock) =>
        {
            await auth.RequireAdminAsync();
            context.Response.Headers.CacheControl = "no-store";
            var requested = int.TryParse(context.Request.Query["limit"], out var parsed) ? parsed : 30;
            var limit = Math.Clamp(requested, 1, 100);
            var items = await ingestions.ListAsync(limit);
            var stats = await ingestions.StatsSinceAsync(clock.UnixNow - 7 * 24 * 60 * 60);
            return ApiSupport.Json(new MailIngestionsResponse(items.Select(item => new MailIngestionDto(
                item.Id, item.Mailbox, item.MessageId, item.SenderEmail, item.SenderName, item.Subject,
                item.Status, item.Attempts, item.Error, item.TicketId, item.CreatedAt, item.UpdatedAt,
                item.ExternalId, item.ExternalUrl, item.Target)).ToList(), stats));
        });

        app.MapPost("/api/mail-ingest", async (HttpContext context, IMailIngestionService ingestions,
            AppRuntime runtime, ISecureTokenService tokens) =>
        {
            if (!MailServiceAuthorized(context.Request, runtime, tokens))
                return ApiSupport.Error(401, "Yetkisiz mail servisi.");
            ApiSupport.Limit(context.Request, 150_000, "Mail metni sınırı aşıldı.");
            var input = await ApiSupport.Body<IncomingMail>(context.Request);
            ValidateIncomingMail(input);
            var result = await ingestions.AcceptAsync(input);
            return ApiSupport.Json(result);
        });

        app.MapPost("/api/mail-ingest/retry", async (HttpContext context, IMailIngestionService ingestions,
            AppRuntime runtime, ISecureTokenService tokens) =>
        {
            if (!MailServiceAuthorized(context.Request, runtime, tokens))
                return ApiSupport.Error(401, "Yetkisiz mail servisi.");
            return ApiSupport.Json(new { results = await ingestions.RetryFailedAsync() });
        });
    }

    private static bool MailServiceAuthorized(HttpRequest request, AppRuntime runtime, ISecureTokenService tokens)
    {
        var configured = runtime.MailIngestToken;
        if (configured.Length < 32) return false;
        var provided = request.Headers["x-mail-ingest-token"].ToString();
        return tokens.ConstantTimeEquals(provided, configured);
    }

    private static MailCheckState ParseCheckState(string? value) => value?.ToLowerInvariant() switch
    {
        "connected" => MailCheckState.Connected,
        "error" => MailCheckState.Error,
        "idle" => MailCheckState.Idle,
        _ => throw ApiException.BadRequest("Geçersiz poller durumu.")
    };

    private static void ValidateIncomingMail(IncomingMail input)
    {
        input.Mailbox = Guard.Text(input.Mailbox, 1, 180, "Mail kutusu geçersiz.");
        input.MessageId = Guard.Text(input.MessageId, 1, 500, "Mail kimliği geçersiz.");
        input.SenderEmail = ApiSupport.NormalizeEmail(input.SenderEmail);
        if (!ApiSupport.ValidEmail(input.SenderEmail)) throw ApiException.BadRequest("Gönderen e-postası geçersiz.");
        input.SenderName = (input.SenderName ?? string.Empty).Trim();
        if (input.SenderName.Length > 120) throw ApiException.BadRequest("Gönderen adı çok uzun.");
        input.Subject = (input.Subject ?? string.Empty).Trim();
        if (input.Subject.Length > 500) throw ApiException.BadRequest("Mail konusu çok uzun.");
        input.Body ??= string.Empty;
        if (input.Body.Length > 12_000) throw ApiException.BadRequest("Mail metni sınırı aşıldı.");
        input.AttachmentNames ??= new List<string>();
        if (input.AttachmentNames.Count > 30) throw ApiException.BadRequest("Ek sayısı sınırı aşıldı.");
        input.AttachmentNames = input.AttachmentNames.Select(name => Guard.Text(name, 1, 180, "Ek adı geçersiz.")).ToList();
    }
}
