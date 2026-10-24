using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

public sealed class GraphMailProvider(
    IHttpClientFactory httpClientFactory,
    AppRuntime runtime,
    ILogger<GraphMailProvider> logger) : IMailProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new();
    private static readonly HashSet<string> WellKnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "archive", "clutter", "conflicts", "conversationhistory", "deleteditems", "drafts",
        "inbox", "junkemail", "localfailures", "msgfolderroot", "outbox",
        "recoverableitemsdeletions", "scheduled", "searchfolders", "sentitems",
        "serverfailures", "syncissues", "voicemail"
    };

    public MailProviderKind Provider => MailProviderKind.MicrosoftGraph;

    public async Task<MailboxPollStatus> PollAsync(MailboxOptions account, MailOptions options, IMailIngestionService ingestion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.User) || string.IsNullOrWhiteSpace(account.TenantId) ||
            string.IsNullOrWhiteSpace(account.ClientId) || string.IsNullOrWhiteSpace(account.ClientSecret))
            throw ApiException.BadRequest($"{account.Label} için Microsoft Graph kiracı, istemci ve kullanıcı bilgileri eksik.");

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);
        var token = await GetTokenAsync(client, account, ct);

        var statePath = MailStateFile.Path(runtime, account);
        var state = MailStateFile.Read<GraphState>(statePath);
        if (state.Initialized && state.ImportExisting != options.ImportExisting)
        {
            state.Cursor = options.ImportExisting ? null : DateTimeOffset.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            state.ImportExisting = options.ImportExisting;
            await MailStateFile.WriteAsync(statePath, state, ct);
            if (!options.ImportExisting)
                return Status(MailCheckState.Connected, "Microsoft Graph bağlantısı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.");
        }

        if (!state.Initialized)
        {
            state.Initialized = true;
            state.ImportExisting = options.ImportExisting;
            state.Cursor = options.ImportExisting ? null : DateTimeOffset.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            await MailStateFile.WriteAsync(statePath, state, ct);
            if (!options.ImportExisting)
                return Status(MailCheckState.Connected, "Microsoft Graph bağlantısı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.");
        }

        if (!state.ImportExisting &&
            (string.IsNullOrWhiteSpace(state.Cursor) ||
             !DateTimeOffset.TryParse(state.Cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)))
        {
            state.Cursor = DateTimeOffset.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            await MailStateFile.WriteAsync(statePath, state, ct);
            return Status(MailCheckState.Connected, "Microsoft Graph bağlantısı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.");
        }

        if (state.Cursor is not null &&
            DateTimeOffset.TryParse(state.Cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cursorValue) &&
            cursorValue > DateTimeOffset.UtcNow)
        {
            state.Cursor = DateTimeOffset.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            await MailStateFile.WriteAsync(statePath, state, ct);
        }

        var maxPerPoll = Math.Clamp(options.MaxPerPoll, 1, 100);
        var processed = 0;
        var skipped = 0;
        var nextUrl = BuildMessageUrl(account, state.Cursor, maxPerPoll);

        while (!string.IsNullOrEmpty(nextUrl) && processed < maxPerPoll)
        {
            using var document = await GetJsonAsync(client, token, nextUrl, ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array) break;

            foreach (var message in value.EnumerateArray())
            {
                if (ct.IsCancellationRequested || processed >= maxPerPoll) break;
                var received = ReadDate(message, "receivedDateTime");
                if (state.Cursor is not null && received is not null &&
                    DateTimeOffset.TryParse(state.Cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cursor) &&
                    received.Value < cursor)
                    continue;

                processed++;
                try
                {
                    var sender = ReadSender(message);
                    if (sender is null)
                    {
                        logger.LogInformation("Graph mail skipped: sender address missing.");
                        skipped++;
                    }
                    else
                    {
                        var messageId = ReadString(message, "internetMessageId");
                        if (string.IsNullOrWhiteSpace(messageId))
                            messageId = $"graph:{ReadString(message, "id")}";
                        var hasAttachments = !message.TryGetProperty("hasAttachments", out var has) || has.ValueKind != JsonValueKind.False;
                        var attachmentNames = hasAttachments
                            ? await ReadAttachmentNamesAsync(client, token, account, ReadString(message, "id"), ct)
                            : new List<string>();
                        var headers = ReadHeaders(message);
                        var payload = new IncomingMail
                        {
                            Mailbox = string.IsNullOrWhiteSpace(account.Label) ? account.User : $"{account.Label} <{account.User}>",
                            MessageId = MailMessageText.Truncate(messageId!, 500),
                            SenderEmail = sender.Value.Address.ToLowerInvariant(),
                            SenderName = MailMessageText.CleanName(sender.Value.Name),
                            Subject = MailMessageText.Truncate(MailMessageText.CleanName(ReadString(message, "subject")), 500),
                            Body = ReadBody(message),
                            AttachmentNames = attachmentNames,
                            AutoSubmitted = AutoSubmitted(headers),
                            Bulk = Bulk(headers),
                            ListUnsubscribe = headers.ContainsKey("List-Unsubscribe")
                        };
                        var outcome = await ingestion.AcceptAsync(payload, ct);
                        logger.LogInformation("Graph mail processed. Account {Account}, Message {Message}, Status {Status}, Ticket {Ticket}",
                            account.Id, payload.MessageId, outcome.Status, outcome.TicketId);
                    }
                }
                catch (ApiException exception) when (exception.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 408 and not 409 and not 425 and not 429)
                {
                    logger.LogWarning("Graph mail permanently rejected; advancing cursor: {Error}", exception.Message);
                    skipped++;
                }
                catch (FormatException exception)
                {
                    logger.LogWarning(exception, "Graph mail could not be parsed; advancing cursor.");
                    skipped++;
                }

                if (received is not null)
                {
                    var normalized = received.Value.ToUniversalTime();
                    if (normalized > DateTimeOffset.UtcNow) normalized = DateTimeOffset.UtcNow;
                    if (state.Cursor is null ||
                        !DateTimeOffset.TryParse(state.Cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var previous) ||
                        normalized > previous)
                    {
                        state.Cursor = normalized.ToString("O", CultureInfo.InvariantCulture);
                        await MailStateFile.WriteAsync(statePath, state, ct);
                    }
                }
            }

            nextUrl = root.TryGetProperty("@odata.nextLink", out var nextLink) ? nextLink.GetString() : null;
        }

        var text = processed > 0
            ? $"{processed} e-posta kontrol edildi{(skipped > 0 ? $", {skipped} tanesi atlandı" : string.Empty)}."
            : "Microsoft Graph bağlantısı başarılı. Yeni e-posta yok.";
        return Status(MailCheckState.Connected, text);
    }

    internal static string NormalizeFolder(string? folder)
    {
        var value = (folder ?? string.Empty).Trim();
        if (value.Length == 0) return "inbox";
        return WellKnownFolders.Contains(value) ? value.ToLowerInvariant() : value;
    }

    private static string BuildMessageUrl(MailboxOptions account, string? cursor, int top)
    {
        var folder = NormalizeFolder(account.Mailbox);
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(account.User)}/mailFolders/{Uri.EscapeDataString(folder)}/messages" +
            $"?$top={Math.Clamp(top, 1, 100)}" +
            "&$select=id,internetMessageId,receivedDateTime,subject,from,body,hasAttachments,internetMessageHeaders" +
            "&$orderby=receivedDateTime%20asc";
        if (!string.IsNullOrWhiteSpace(cursor) &&
            DateTimeOffset.TryParse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            var from = value.AddSeconds(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            url += $"&$filter=receivedDateTime%20gt%20{from}";
        }
        return url;
    }

    private async Task<string> GetTokenAsync(HttpClient client, MailboxOptions account, CancellationToken ct)
    {
        var key = Hash($"{account.TenantId}|{account.ClientId}|{account.ClientSecret}");
        if (TokenCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.Value;

        var endpoint = $"https://login.microsoftonline.com/{Uri.EscapeDataString(account.TenantId)}/oauth2/v2.0/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = account.ClientId,
                ["client_secret"] = account.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            })
        };
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw ApiException.BadRequest($"Microsoft Graph kimlik doğrulaması başarısız: {ErrorMessage(body, "Microsoft 365")}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken.GetString()))
            throw ApiException.BadRequest("Microsoft Graph erişim belirteci alınamadı.");
        var expires = root.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds))
            : DateTimeOffset.UtcNow.AddMinutes(50);
        var token = accessToken.GetString()!;
        TokenCache[key] = new CachedToken(token, expires);
        return token;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string token, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ApiException($"Microsoft Graph isteği başarısız: {ErrorMessage(body, "Graph")}", (int)response.StatusCode);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("Microsoft Graph beklenmeyen bir yanıt döndürdü.");
        }
    }

    private static async Task<List<string>> ReadAttachmentNamesAsync(HttpClient client, string token, MailboxOptions account, string? messageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return new List<string>();
        try
        {
            var url = $"{GraphBase}/users/{Uri.EscapeDataString(account.User)}/messages/{Uri.EscapeDataString(messageId)}/attachments?$select=name&$top=30";
            using var document = await GetJsonAsync(client, token, url, ct);
            if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                return new List<string>();
            return value.EnumerateArray()
                .Select(item => MailMessageText.CleanName(ReadString(item, "name")))
                .Where(name => name.Length > 0).Take(30).ToList();
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 403 or 404)
        {
            return new List<string>();
        }
    }

    private static string ReadBody(JsonElement message)
    {
        if (!message.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
            return string.Empty;
        var content = ReadString(body, "content") ?? string.Empty;
        var contentType = ReadString(body, "contentType") ?? "text";
        var text = contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
            ? MailMessageText.HtmlToText(content)
            : content.Trim();
        return MailMessageText.Truncate(text, 12_000);
    }

    private static (string Address, string Name)? ReadSender(JsonElement message)
    {
        if (!message.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object) return null;
        if (!from.TryGetProperty("emailAddress", out var address) || address.ValueKind != JsonValueKind.Object) return null;
        var value = ReadString(address, "address");
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@') || value.Any(char.IsWhiteSpace)) return null;
        return (value, ReadString(address, "name") ?? string.Empty);
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement message)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!message.TryGetProperty("internetMessageHeaders", out var value) || value.ValueKind != JsonValueKind.Array)
            return headers;
        foreach (var item in value.EnumerateArray())
        {
            var name = ReadString(item, "name");
            var headerValue = ReadString(item, "value");
            if (!string.IsNullOrWhiteSpace(name) && headerValue is not null) headers[name] = headerValue;
        }
        return headers;
    }

    private static bool AutoSubmitted(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Auto-Submitted", out var value) && value.Length > 0 &&
        !value.Contains("no", StringComparison.OrdinalIgnoreCase);

    private static bool Bulk(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Precedence", out var value) &&
        System.Text.RegularExpressions.Regex.IsMatch(value, "^(bulk|junk|list)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        var value = ReadString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ErrorMessage(string body, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error_description", out var description) && description.ValueKind == JsonValueKind.String)
                return MailMessageText.Truncate(description.GetString() ?? source, 500);
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return MailMessageText.Truncate(error.GetString() ?? source, 500);
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return MailMessageText.Truncate(message.GetString() ?? source, 500);
            }
        }
        catch (JsonException) { }
        return MailMessageText.Truncate(string.IsNullOrWhiteSpace(body) ? $"{source} isteği başarısız." : body, 500);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static MailboxPollStatus Status(MailCheckState state, string message) => new()
    {
        State = state,
        CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Message = message
    };

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    private sealed class GraphState
    {
        public string? Cursor { get; set; }
        public bool Initialized { get; set; }
        public bool ImportExisting { get; set; }
    }
}
