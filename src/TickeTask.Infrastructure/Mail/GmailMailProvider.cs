using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimeKit;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

public sealed class GmailMailProvider(
    IHttpClientFactory httpClientFactory,
    AppRuntime runtime,
    ILogger<GmailMailProvider> logger) : IMailProvider
{
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    private const string ReadOnlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new();

    private static string UserBase(MailboxOptions account) =>
        $"https://gmail.googleapis.com/gmail/v1/users/{Uri.EscapeDataString(account.User)}";

    public MailProviderKind Provider => MailProviderKind.Gmail;

    public async Task<MailboxPollStatus> PollAsync(MailboxOptions account, MailOptions options, IMailIngestionService ingestion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.User))
            throw ApiException.BadRequest($"{account.Label} için Google Workspace kullanıcı adresi eksik.");
        if (string.IsNullOrWhiteSpace(account.ServiceAccountJson) &&
            (string.IsNullOrWhiteSpace(account.ClientId) || string.IsNullOrWhiteSpace(account.ClientSecret) || string.IsNullOrWhiteSpace(account.RefreshToken)))
            throw ApiException.BadRequest($"{account.Label} için Google OAuth istemci bilgileri veya hizmet hesabı JSON dosyası gerekli.");

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(90);
        var token = await GetTokenAsync(client, account, ct);

        var statePath = MailStateFile.Path(runtime, account);
        var state = MailStateFile.Read<GmailState>(statePath);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var initialCursor = Math.Max(0, now - 1);

        if (!state.Initialized)
        {
            state.Initialized = true;
            state.ImportExisting = options.ImportExisting;
            state.StartedAt = now;
            state.Cursor = initialCursor;
            state.ImportBackfill = options.ImportExisting;
            state.BackfillBefore = options.ImportExisting ? now : 0;
            await MailStateFile.WriteAsync(statePath, state, ct);
            if (!options.ImportExisting)
                return Status(MailCheckState.Connected, "Gmail bağlantısı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.");
        }

        if (state.ImportExisting != options.ImportExisting)
        {
            state.ImportExisting = options.ImportExisting;
            state.ImportBackfill = options.ImportExisting;
            state.StartedAt = now;
            state.Cursor = initialCursor;
            state.BackfillBefore = options.ImportExisting ? now : 0;
            await MailStateFile.WriteAsync(statePath, state, ct);
            if (!options.ImportExisting)
                return Status(MailCheckState.Connected, "Gmail bağlantısı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.");
        }

        var repaired = false;
        if (state.Cursor <= 0 || state.Cursor > now)
        {
            state.Cursor = initialCursor;
            repaired = true;
        }
        if (state.StartedAt <= 0 || state.StartedAt > now)
        {
            state.StartedAt = now;
            repaired = true;
        }
        if (state.ImportBackfill && (state.BackfillBefore <= 0 || state.BackfillBefore > now))
        {
            state.BackfillBefore = now;
            repaired = true;
        }
        if (repaired) await MailStateFile.WriteAsync(statePath, state, ct);

        var labelId = await ResolveLabelIdAsync(client, token, account, ct);
        if (state.ImportBackfill)
        {
            var result = await BackfillAsync(client, token, account, options, ingestion, state, statePath, labelId, ct);
            if (result is not null) return result;
        }

        return await LiveAsync(client, token, account, options, ingestion, state, statePath, labelId, ct);
    }

    private async Task<string?> ResolveLabelIdAsync(HttpClient client, string token, MailboxOptions account, CancellationToken ct)
    {
        var value = account.Mailbox.Trim();
        if (value.Length == 0) return null;
        if (GmailLabels.TryNormalizeSystemLabelId(value, out var systemId)) return systemId;
        if (GmailLabels.IsCustomLabelId(value)) return value;

        var url = $"{UserBase(account)}/labels?fields=labels(id,name)";
        using var document = await GetJsonAsync(client, token, url, ct);
        if (!document.RootElement.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
            throw ApiException.BadRequest($"Gmail etiketi bulunamadı: {value}. Gmail etiket kimliğini veya görünen adını girin.");
        var pairs = labels.EnumerateArray()
            .Select(label => (Id: ReadString(label, "id") ?? string.Empty, Name: ReadString(label, "name") ?? string.Empty));
        var labelId = GmailLabels.FindByName(pairs, value);
        if (string.IsNullOrWhiteSpace(labelId))
            throw ApiException.BadRequest($"Gmail etiketi bulunamadı: {value}. Gmail etiket kimliğini veya görünen adını girin.");
        return labelId;
    }

    private async Task<MailboxPollStatus?> BackfillAsync(
        HttpClient client,
        string token,
        MailboxOptions account,
        MailOptions options,
        IMailIngestionService ingestion,
        GmailState state,
        string statePath,
        string? labelId,
        CancellationToken ct)
    {
        if (state.BackfillBefore <= 0)
        {
            state.ImportBackfill = false;
            state.BackfillBefore = 0;
            state.Cursor = Math.Max(0, state.StartedAt - 1);
            await MailStateFile.WriteAsync(statePath, state, ct);
            return Status(MailCheckState.Connected, "Geçmiş e-postalar tarandı. Yeni e-postalar izlenecek.");
        }
        var query = $"before:{state.BackfillBefore}";
        var ids = await ListMessageIdsAsync(client, token, account, query, Math.Clamp(options.MaxPerPoll, 1, 100), labelId, ct);
        if (ids.Count == 0)
        {
            state.ImportBackfill = false;
            state.BackfillBefore = 0;
            state.Cursor = Math.Max(0, state.StartedAt - 1);
            await MailStateFile.WriteAsync(statePath, state, ct);
            return Status(MailCheckState.Connected, "Geçmiş e-postalar tarandı. Yeni e-postalar izlenecek.");
        }

        var messages = await LoadMessagesAsync(client, token, account, ids, ct);
        if (messages.Count == 0)
        {
            throw ApiException.BadRequest(
                "Gmail geçmiş e-postaları okunamadı; Gmail izinlerini, etiketi ve mesajların silinip silinmediğini kontrol edin. Bu turda ilerleme kaydedilmedi.");
        }

        var processed = await ProcessAsync(messages, account, ingestion, ct);
        var oldest = messages.Min(item => item.InternalDate);
        var nextBefore = oldest > 0 ? oldest : 0;
        if (nextBefore >= state.BackfillBefore && state.BackfillBefore > 0)
            nextBefore = Math.Max(0, state.BackfillBefore - 1);
        state.BackfillBefore = nextBefore;
        if (state.BackfillBefore <= 0)
        {
            state.ImportBackfill = false;
            state.Cursor = Math.Max(0, state.StartedAt - 1);
        }
        await MailStateFile.WriteAsync(statePath, state, ct);
        return Status(MailCheckState.Connected,
            $"{processed.Processed} geçmiş e-posta işlendi{(processed.Skipped > 0 ? $", {processed.Skipped} tanesi atlandı" : string.Empty)}.");
    }

    private async Task<MailboxPollStatus> LiveAsync(
        HttpClient client,
        string token,
        MailboxOptions account,
        MailOptions options,
        IMailIngestionService ingestion,
        GmailState state,
        string statePath,
        string? labelId,
        CancellationToken ct)
    {
        var maxPerPoll = Math.Clamp(options.MaxPerPoll, 1, 100);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var query = $"after:{state.Cursor} before:{now + 1}";
        var ids = await OldestMessageIdsAsync(client, token, account, query, maxPerPoll, labelId, ct);
        if (ids.Count == 0)
            return Status(MailCheckState.Connected, "Gmail bağlantısı başarılı. Yeni e-posta yok.");

        var messages = await LoadMessagesAsync(client, token, account, ids, ct);
        if (messages.Count == 0)
        {
            state.Cursor = Math.Min(now, state.Cursor + 1);
            await MailStateFile.WriteAsync(statePath, state, ct);
            logger.LogWarning("Gmail returned {Count} message ids but none could be loaded; advancing cursor by one second to avoid a permanent stall.", ids.Count);
            return Status(MailCheckState.Connected, "Gmail bağlantısı başarılı. Bu turda okunabilir e-posta bulunamadı.");
        }

        var processed = await ProcessAsync(messages, account, ingestion, ct);
        var newest = Math.Min(messages.Max(item => item.InternalDate), now);
        if (newest > state.Cursor)
        {
            state.Cursor = newest;
            await MailStateFile.WriteAsync(statePath, state, ct);
        }
        return Status(MailCheckState.Connected,
            $"{processed.Processed} e-posta kontrol edildi{(processed.Skipped > 0 ? $", {processed.Skipped} tanesi atlandı" : string.Empty)}.");
    }

    private sealed record LoadedMessage(string GmailId, long InternalDate, MimeMessage? Message);

    private async Task<(int Processed, int Skipped)> ProcessAsync(
        List<LoadedMessage> messages,
        MailboxOptions account,
        IMailIngestionService ingestion,
        CancellationToken ct)
    {
        var processed = 0;
        var skipped = 0;
        foreach (var item in messages.OrderBy(message => message.InternalDate))
        {
            if (ct.IsCancellationRequested) break;
            var message = item.Message;
            if (message is null)
            {
                logger.LogWarning("Gmail message {MessageId} could not be parsed and was skipped.", item.GmailId);
                skipped++;
                continue;
            }
            var sender = MailMessageText.SingleSender(message);
            if (sender is null)
            {
                logger.LogInformation("Gmail message skipped: single sender address missing.");
                skipped++;
                continue;
            }
            try
            {
                var messageId = string.IsNullOrWhiteSpace(message.MessageId)
                    ? $"gmail:{item.GmailId}"
                    : message.MessageId;
                var payload = new IncomingMail
                {
                    Mailbox = string.IsNullOrWhiteSpace(account.Label) ? account.User : $"{account.Label} <{account.User}>",
                    MessageId = MailMessageText.Truncate(messageId!, 500),
                    SenderEmail = sender.Address.ToLowerInvariant(),
                    SenderName = MailMessageText.CleanName(sender.Name),
                    Subject = MailMessageText.Truncate(MailMessageText.CleanName(message.Subject), 500),
                    Body = MailMessageText.PlainBody(message),
                    AttachmentNames = message.Attachments
                        .Select(attachment => MailMessageText.CleanName(attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name))
                        .Where(name => name.Length > 0).Take(30).ToList(),
                    AutoSubmitted = MailMessageText.AutoSubmitted(message),
                    Bulk = MailMessageText.Bulk(message),
                    ListUnsubscribe = message.Headers.Contains(HeaderId.ListUnsubscribe)
                };
                var outcome = await ingestion.AcceptAsync(payload, ct);
                logger.LogInformation("Gmail message processed. Account {Account}, Message {Message}, Status {Status}, Ticket {Ticket}",
                    account.Id, payload.MessageId, outcome.Status, outcome.TicketId);
                processed++;
            }
            catch (ApiException exception) when (exception.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 408 and not 409 and not 425 and not 429)
            {
                logger.LogWarning("Gmail message permanently rejected; advancing cursor: {Error}", exception.Message);
                skipped++;
            }
            catch (FormatException exception)
            {
                logger.LogWarning(exception, "Gmail message could not be parsed; advancing cursor.");
                skipped++;
            }
        }
        return (processed, skipped);
    }

    private async Task<List<string>> OldestMessageIdsAsync(
        HttpClient client,
        string token,
        MailboxOptions account,
        string query,
        int pageSize,
        string? labelId,
        CancellationToken ct)
    {
        string? pageToken = null;
        List<string> lastPage = new();
        var pages = 0;
        do
        {
            var url = BuildListUrl(account, query, pageSize, labelId, pageToken);
            using var document = await GetJsonAsync(client, token, url, ct);
            var root = document.RootElement;
            lastPage = ReadIds(root);
            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (++pages > 1000)
                throw ApiException.BadRequest("Gmail mesaj listesi çok fazla sayfaya ayrıldı; tur başına en fazla e-posta değerini artırın veya etiketi daraltın.");
        }
        while (!string.IsNullOrEmpty(pageToken));
        return lastPage;
    }

    private async Task<List<string>> ListMessageIdsAsync(
        HttpClient client,
        string token,
        MailboxOptions account,
        string query,
        int pageSize,
        string? labelId,
        CancellationToken ct)
    {
        var url = BuildListUrl(account, query, pageSize, labelId, null);
        using var document = await GetJsonAsync(client, token, url, ct);
        return ReadIds(document.RootElement);
    }

    private static List<string> ReadIds(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return messages.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    private async Task<List<LoadedMessage>> LoadMessagesAsync(HttpClient client, string token, MailboxOptions account, List<string> ids, CancellationToken ct)
    {
        var result = new List<LoadedMessage>();
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            LoadedMessage? loaded = null;
            try
            {
                loaded = await TryLoadMessageAsync(client, token, account, id, ct);
            }
            catch (ApiException exception) when (IsPermanentFailure(exception))
            {
                logger.LogWarning("Gmail message {MessageId} could not be loaded: {Error}", id, exception.Message);
            }
            if (loaded is not null)
            {
                result.Add(loaded);
                continue;
            }

            var internalDate = await TryReadInternalDateAsync(client, token, account, id, ct);
            if (internalDate > 0)
            {
                result.Add(new LoadedMessage(id, internalDate, null));
                logger.LogWarning("Gmail message {MessageId} could not be parsed; its date was recorded so polling can continue.", id);
            }
            else
            {
                logger.LogWarning("Gmail message {MessageId} could not be loaded or dated; it was skipped.", id);
            }
        }
        return result;
    }

    private async Task<LoadedMessage?> TryLoadMessageAsync(HttpClient client, string token, MailboxOptions account, string id, CancellationToken ct)
    {
        var url = $"{UserBase(account)}/messages/{Uri.EscapeDataString(id)}?format=raw";
        using var document = await GetJsonAsync(client, token, url, ct);
        var root = document.RootElement;
        if (!root.TryGetProperty("raw", out var raw) || raw.ValueKind != JsonValueKind.String) return null;
        var bytes = DecodeBase64Url(raw.GetString() ?? string.Empty);
        if (bytes.Length == 0) return null;

        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(bytes);
            message = MimeMessage.Load(stream);
        }
        catch (Exception exception) when (exception is FormatException or IOException)
        {
            logger.LogWarning(exception, "Gmail message {MessageId} could not be parsed.", id);
            return null;
        }

        var internalDate = ReadInternalDate(root);
        if (internalDate <= 0)
        {
            var date = message.Date.ToUnixTimeSeconds();
            if (date > 0) internalDate = date;
        }
        return internalDate > 0 ? new LoadedMessage(id, internalDate, message) : null;
    }

    private async Task<long> TryReadInternalDateAsync(HttpClient client, string token, MailboxOptions account, string id, CancellationToken ct)
    {
        try
        {
            var url = $"{UserBase(account)}/messages/{Uri.EscapeDataString(id)}?format=minimal";
            using var document = await GetJsonAsync(client, token, url, ct);
            return ReadInternalDate(document.RootElement);
        }
        catch (ApiException exception) when (IsPermanentFailure(exception))
        {
            return 0;
        }
    }

    internal static long ReadInternalDate(JsonElement root)
    {
        if (!root.TryGetProperty("internalDate", out var stamp)) return 0;
        long milliseconds;
        if (stamp.ValueKind == JsonValueKind.Number)
        {
            if (!stamp.TryGetInt64(out milliseconds)) return 0;
        }
        else if (stamp.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(stamp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)) return 0;
        }
        else
        {
            return 0;
        }
        if (milliseconds <= 0) return 0;
        var seconds = milliseconds > 100_000_000_000 ? milliseconds / 1000 : milliseconds;
        return seconds > 0 ? seconds : 0;
    }

    private static bool IsPermanentFailure(ApiException exception) =>
        exception.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 408 and not 409 and not 425 and not 429;

    private static string BuildListUrl(MailboxOptions account, string query, int pageSize, string? labelId, string? pageToken)
    {
        var url = $"{UserBase(account)}/messages?maxResults={Math.Clamp(pageSize, 1, 100)}&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(labelId))
            url += $"&labelIds={Uri.EscapeDataString(labelId)}";
        if (!string.IsNullOrWhiteSpace(pageToken))
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        return url;
    }

    private async Task<string> GetTokenAsync(HttpClient client, MailboxOptions account, CancellationToken ct)
    {
        var key = Hash($"{account.User}|{account.ClientId}|{account.ClientSecret}|{account.RefreshToken}|{account.ServiceAccountJson}");
        if (TokenCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return cached.Value;

        string tokenUri;
        FormUrlEncodedContent form;
        if (!string.IsNullOrWhiteSpace(account.ServiceAccountJson))
        {
            var serviceAccount = GoogleServiceAccountJson.Parse(account.ServiceAccountJson);
            tokenUri = string.IsNullOrWhiteSpace(serviceAccount.TokenUri) ? DefaultTokenUri : serviceAccount.TokenUri;
            var assertion = CreateServiceAccountAssertion(serviceAccount, account.User, tokenUri);
            form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            });
        }
        else
        {
            tokenUri = DefaultTokenUri;
            form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = account.ClientId,
                ["client_secret"] = account.ClientSecret,
                ["refresh_token"] = account.RefreshToken,
                ["grant_type"] = "refresh_token"
            });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri) { Content = form };
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw ApiException.BadRequest($"Google kimlik doğrulaması başarısız: {ErrorMessage(body, "Google")}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken.GetString()))
            throw ApiException.BadRequest("Google erişim belirteci alınamadı.");
        var expires = root.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt32(out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds))
            : DateTimeOffset.UtcNow.AddMinutes(50);
        var token = accessToken.GetString()!;
        TokenCache[key] = new CachedToken(token, expires);
        return token;
    }

    private static string CreateServiceAccountAssertion(GoogleServiceAccount account, string impersonatedUser, string tokenUri)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })));
        var payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = account.ClientEmail,
            scope = ReadOnlyScope,
            aud = tokenUri,
            exp = now + 3600,
            iat = now,
            sub = impersonatedUser
        })));
        var signingInput = $"{header}.{payload}";
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKey);
            var signature = Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return $"{signingInput}.{signature}";
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw ApiException.BadRequest("Google hizmet hesabı özel anahtarı okunamadı.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(HttpClient client, string token, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ApiException($"Gmail isteği başarısız: {ErrorMessage(body, "Gmail")}", (int)response.StatusCode);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("Gmail beklenmeyen bir yanıt döndürdü.");
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<byte>();
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static MailboxPollStatus Status(MailCheckState state, string message) => new()
    {
        State = state,
        CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Message = message
    };

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

    private sealed class GmailState
    {
        public long Cursor { get; set; }
        public long BackfillBefore { get; set; }
        public long StartedAt { get; set; }
        public bool ImportBackfill { get; set; }
        public bool Initialized { get; set; }
        public bool ImportExisting { get; set; }
    }
}
