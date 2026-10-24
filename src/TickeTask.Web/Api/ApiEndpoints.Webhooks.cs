using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static void MapWebhooks(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/inbound", async (HttpContext context, IMessageIngestionService ingestion,
            AppRuntime runtime, ISecureTokenService tokens, IRateLimiter limiter, SessionAuthService auth) =>
        {
            var token = runtime.InboundWebhookToken;
            var provided = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(token) || !tokens.ConstantTimeEquals(provided, $"Bearer {token}"))
                return ApiSupport.Error(401, "Geçersiz webhook anahtarı.");
            ApiSupport.Limit(context.Request, 100_000, "Webhook isteği çok büyük.");
            ApiSupport.RateLimited(limiter, $"inbound-webhook:{auth.ClientAddress()}", 120, 60);
            var input = await ApiSupport.Body<InboundWebhookRequest>(context.Request);
            var customerName = (input.CustomerName ?? string.Empty).Trim();
            if (customerName.Length > 120) throw ApiException.BadRequest("Müşteri adı çok uzun.");
            var email = (input.Email ?? string.Empty).Trim();
            if (email.Length > 0 && (!ApiSupport.ValidEmail(email) || email.Length > 254)) throw ApiException.BadRequest("E-posta geçersiz.");
            var phone = (input.Phone ?? string.Empty).Trim();
            if (phone.Length > 40) throw ApiException.BadRequest("Telefon çok uzun.");
            var sender = (input.Sender ?? string.Empty).Trim();
            if (sender.Length > 254) throw ApiException.BadRequest("Gönderen çok uzun.");
            var subject = (input.Subject ?? string.Empty).Trim();
            if (subject.Length > 500) throw ApiException.BadRequest("Konu çok uzun.");
            var externalId = (input.ExternalId ?? string.Empty).Trim();
            if (externalId.Length > 500) throw ApiException.BadRequest("Dış kimlik çok uzun.");
            var content = Guard.Text(input.Content, 1, 12_000, "Mesaj içeriği gerekli.");
            var channel = (input.Channel ?? "webhook").ToLowerInvariant() switch
            {
                "email" => MessageChannel.Email,
                "whatsapp" => MessageChannel.Whatsapp,
                "webhook" => MessageChannel.Webhook,
                _ => throw ApiException.BadRequest("Kanal geçersiz.")
            };
            var result = await ingestion.IngestAsync(
                runtime.WebhookOwnerId,
                customerName.Length == 0 ? null : customerName,
                email.Length == 0 ? null : email,
                phone.Length == 0 ? null : phone,
                channel,
                sender.Length == 0 ? null : sender,
                subject.Length == 0 ? null : subject,
                content,
                externalId.Length == 0 ? null : externalId);
            return ApiSupport.Json(new { id = result.Id, duplicate = result.Duplicate }, result.Duplicate ? 200 : 201);
        });

        app.MapGet("/api/webhooks/whatsapp", (HttpContext context, AppRuntime runtime, ISecureTokenService tokens) =>
        {
            var configured = runtime.WhatsAppVerifyToken;
            var provided = context.Request.Query["hub.verify_token"].ToString();
            if (string.IsNullOrWhiteSpace(configured) || !tokens.ConstantTimeEquals(provided, configured))
                return ApiSupport.Error(403, "Doğrulama başarısız.");
            if (context.Request.Query["hub.mode"] != "subscribe")
                return ApiSupport.Error(400, "Doğrulama modu geçersiz.");
            context.Response.Headers.CacheControl = "no-store";
            return Results.Text(context.Request.Query["hub.challenge"].ToString());
        });

        app.MapPost("/api/webhooks/whatsapp", async (HttpContext context, IMessageIngestionService ingestion,
            AppRuntime runtime, ISecureTokenService tokens, IRateLimiter limiter, SessionAuthService auth) =>
        {
            try
            {
                if (context.Request.ContentLength is > 1_000_000) return ApiSupport.Error(413, "WhatsApp isteği çok büyük.");
                ApiSupport.RateLimited(limiter, $"whatsapp:{auth.ClientAddress()}", 120, 60);
                string body;
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8)) body = await reader.ReadToEndAsync();
                if (body.Length > 1_000_000) return ApiSupport.Error(413, "WhatsApp isteği çok büyük.");
                VerifyWhatsAppSignature(body, context.Request.Headers["x-hub-signature-256"].ToString(), runtime.WhatsAppAppSecret, tokens);

                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var ownerId = runtime.WebhookOwnerId;
                var value = root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0
                    ? entries[0].TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array && changes.GetArrayLength() > 0
                        ? changes[0].TryGetProperty("value", out var changeValue) ? changeValue : default
                        : default
                    : default;
                var contacts = new Dictionary<string, string>(StringComparer.Ordinal);
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("contacts", out var contactItems) && contactItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contact in contactItems.EnumerateArray())
                    {
                        var waId = contact.TryGetProperty("wa_id", out var waValue) ? waValue.GetString() ?? string.Empty : string.Empty;
                        var name = contact.TryGetProperty("profile", out var profile) && profile.TryGetProperty("name", out var nameValue)
                            ? nameValue.GetString() ?? waId
                            : waId;
                        if (waId.Length > 0) contacts[waId] = name;
                    }
                }
                var received = 0;
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("messages", out var messageItems) && messageItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messageItems.EnumerateArray())
                    {
                        if (!message.TryGetProperty("text", out var text) || !text.TryGetProperty("body", out var textBody)) continue;
                        var content = textBody.GetString();
                        if (string.IsNullOrWhiteSpace(content)) continue;
                        var from = message.TryGetProperty("from", out var fromValue) ? fromValue.GetString() ?? string.Empty : string.Empty;
                        var id = message.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                        if (from.Length == 0) continue;
                        contacts.TryGetValue(from, out var customerName);
                        await ingestion.IngestAsync(ownerId, customerName ?? from, null, from, MessageChannel.Whatsapp, from, null, content, id.Length == 0 ? null : id);
                        received++;
                    }
                }
                return ApiSupport.Json(new { received });
            }
            catch (JsonException)
            {
                return ApiSupport.Error(400, "Geçersiz WhatsApp yükü.");
            }
        });
    }

    private static void VerifyWhatsAppSignature(string body, string signature, string secret, ISecureTokenService tokens)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ApiException("WhatsApp entegrasyonu yapılandırılmamış.", 401);
        if (!signature.StartsWith("sha256=", StringComparison.Ordinal)) throw new ApiException("WhatsApp imzası eksik.", 401);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (!tokens.ConstantTimeEquals(expected, signature)) throw new ApiException("WhatsApp imzası geçersiz.", 401);
    }
}
