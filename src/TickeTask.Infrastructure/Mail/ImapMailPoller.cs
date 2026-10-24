using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

public sealed class ImapMailPoller(
    IServiceScopeFactory scopeFactory,
    AppRuntime runtime,
    ILogger<ImapMailPoller> logger) : BackgroundService
{
    private const int PollIntervalMilliseconds = 15_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Mail poller failed");
            }
            try { await Task.Delay(PollIntervalMilliseconds, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IMailConfigService>();
        var ingestion = scope.ServiceProvider.GetRequiredService<IMailIngestionService>();
        var settings = await config.GetAsync(ct);
        if (settings is null || !settings.Enabled || settings.Mailboxes.Count == 0)
        {
            await config.SetPollStatusAsync(new MailPollStatus
            {
                State = MailCheckState.Idle,
                CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = "Mail kanalı kapalı veya yapılandırılmadı.",
                Mailboxes = new Dictionary<string, MailboxPollStatus>()
            }, ct);
            return;
        }

        var results = new Dictionary<string, MailboxPollStatus>();
        foreach (var mailbox in settings.Mailboxes)
        {
            if (ct.IsCancellationRequested) break;
            try { results[mailbox.Id] = await PollAccountAsync(mailbox, settings, ingestion, ct); }
            catch (Exception exception)
            {
                var message = Truncate(exception.Message, 500);
                results[mailbox.Id] = new MailboxPollStatus
                {
                    State = MailCheckState.Error,
                    CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Message = message
                };
                logger.LogError(exception, "Mail account poll failed: {Account}", mailbox.Id);
            }
        }

        var errors = results.Values.Count(result => result.State == MailCheckState.Error);
        await config.SetPollStatusAsync(new MailPollStatus
        {
            State = errors > 0 ? MailCheckState.Error : MailCheckState.Connected,
            CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = errors > 0
                ? $"{errors} mail hesabında hata var; diğerleri kontrol edildi."
                : $"{results.Count} mail hesabı kontrol edildi.",
            Mailboxes = results
        }, ct);

        try { await ingestion.RetryFailedAsync(10, ct); }
        catch (Exception exception) { logger.LogWarning(exception, "Mail retry queue failed"); }
    }

    private async Task<MailboxPollStatus> PollAccountAsync(MailboxOptions account, MailOptions options, IMailIngestionService ingestion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.Host) || string.IsNullOrWhiteSpace(account.User) || string.IsNullOrWhiteSpace(account.Password))
            throw ApiException.BadRequest($"{account.Label} için IMAP bilgileri eksik.");
        using var client = new ImapClient
        {
            Timeout = 120_000,
            ServerCertificateValidationCallback = (sender, certificate, chain, errors) => errors == System.Net.Security.SslPolicyErrors.None
        };
        try
        {
            await client.ConnectAsync(account.Host, account.Port,
                account.Secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
            await client.AuthenticateAsync(account.User, account.Password, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw ApiException.BadRequest($"IMAP bağlantısı kurulamadı: {exception.Message}");
        }

        try
        {
            var folder = await client.GetFolderAsync(account.Mailbox, ct);
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            var statePath = StatePath(account);
            var uidValidity = folder.UidValidity.ToString();
            var uidNext = folder.UidNext?.Id ?? 0;
            var state = ReadState(statePath);

            if (state.Initialized && options.ImportExisting != state.ImportExisting)
            {
                if (options.ImportExisting) state.LastUid = 0;
                state.ImportExisting = options.ImportExisting;
                await SaveStateAsync(statePath, state, ct);
            }

            if (!state.Initialized)
            {
                state.UidValidity = uidValidity;
                state.Initialized = true;
                state.ImportExisting = options.ImportExisting;
                state.LastUid = options.ImportExisting ? 0 : Math.Max(0, (int)uidNext - 1);
                await SaveStateAsync(statePath, state, ct);
                if (!options.ImportExisting)
                    return Status(MailCheckState.Connected, "Bağlantı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.", state.LastUid);
            }

            if (state.UidValidity != uidValidity)
            {
                state.UidValidity = uidValidity;
                state.LastUid = options.ImportExisting ? 0 : Math.Max(0, (int)uidNext - 1);
                state.Initialized = true;
                state.ImportExisting = options.ImportExisting;
                await SaveStateAsync(statePath, state, ct);
                if (!options.ImportExisting)
                    return Status(MailCheckState.Connected, "Mail kutusu sıfırlandı; yeni e-postalar izlenecek.", state.LastUid);
            }

            if (folder.Count == 0)
                return Status(MailCheckState.Connected, "Bağlantı başarılı. Gelen kutusu boş.", state.LastUid);

            var maxPerPoll = Math.Clamp(options.MaxPerPoll, 1, 100);
            var processed = 0;
            var skipped = 0;
            var start = new UniqueId((uint)Math.Max(1, state.LastUid + 1));
            var range = new UniqueIdRange(start, UniqueId.MaxValue);
            var uids = (await folder.SearchAsync(SearchQuery.Uids(range), ct)).OrderBy(uid => uid.Id).ToList();
            foreach (var uid in uids)
            {
                if (ct.IsCancellationRequested || processed >= maxPerPoll) break;
                if (uid.Id <= state.LastUid) continue;
                processed++;
                try
                {
                    var message = await folder.GetMessageAsync(uid, ct);
                    var sender = SingleSender(message);
                    if (message.MessageId is null || message.MessageId.Length > 500 || sender is null)
                    {
                        logger.LogInformation("Mail skipped: missing Message-ID or single sender. UID {Uid}", uid);
                        skipped++;
                    }
                    else
                    {
                        var payload = new IncomingMail
                        {
                            Mailbox = string.IsNullOrWhiteSpace(account.Label) ? account.User : $"{account.Label} <{account.User}>",
                            MessageId = message.MessageId,
                            SenderEmail = sender.Address.ToLowerInvariant(),
                            SenderName = CleanName(sender.Name),
                            Subject = CleanName(message.Subject).Length > 500 ? CleanName(message.Subject)[..500] : CleanName(message.Subject),
                            Body = PlainBody(message),
                            AttachmentNames = message.Attachments.Select(attachment => CleanName(attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name))
                                .Where(name => name.Length > 0).Take(30).ToList(),
                            AutoSubmitted = AutoSubmitted(message),
                            Bulk = Bulk(message),
                            ListUnsubscribe = message.Headers.Contains(HeaderId.ListUnsubscribe)
                        };
                        var outcome = await ingestion.AcceptAsync(payload, ct);
                        logger.LogInformation("Mail processed. Account {Account}, UID {Uid}, Status {Status}, Ticket {Ticket}",
                            account.Id, uid.Id, outcome.Status, outcome.TicketId);
                    }
                }
                catch (ApiException exception) when (exception.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 408 and not 409 and not 425 and not 429)
                {
                    logger.LogWarning("Mail permanently rejected; advancing cursor. UID {Uid}: {Error}", uid.Id, exception.Message);
                    skipped++;
                }
                catch (FormatException exception)
                {
                    logger.LogWarning(exception, "Mail could not be parsed; advancing cursor. UID {Uid}", uid.Id);
                    skipped++;
                }
                state.LastUid = (int)uid.Id;
                await SaveStateAsync(statePath, state, ct);
            }

            var text = processed > 0
                ? $"{processed} e-posta kontrol edildi{(skipped > 0 ? $", {skipped} tanesi atlandı" : string.Empty)}."
                : "Bağlantı başarılı. Yeni e-posta yok.";
            return Status(MailCheckState.Connected, text, state.LastUid);
        }
        finally
        {
            try { await client.DisconnectAsync(true, CancellationToken.None); } catch { }
        }
    }

    private static MailboxPollStatus Status(MailCheckState state, string message, int lastUid) => new()
    {
        State = state,
        CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Message = message,
        LastUid = lastUid
    };

    private static MailboxAddress? SingleSender(MimeMessage message)
    {
        var mailboxes = message.From.Mailboxes.ToList();
        if (mailboxes.Count != 1) return null;
        var sender = mailboxes[0];
        return sender.Address.Contains('@') && !sender.Address.Any(char.IsWhiteSpace) ? sender : null;
    }

    private static bool AutoSubmitted(MimeMessage message)
    {
        var value = message.Headers["Auto-Submitted"] ?? string.Empty;
        return value.Length > 0 && !value.Contains("no", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Bulk(MimeMessage message) =>
        System.Text.RegularExpressions.Regex.IsMatch(message.Headers["Precedence"] ?? string.Empty, "^(bulk|junk|list)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string PlainBody(MimeMessage message)
    {
        var text = message.TextBody?.Trim() ?? string.Empty;
        if (text.Length > 0) return Truncate(text, 12000);
        if (message.HtmlBody is { Length: > 0 } html)
        {
            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);
            foreach (var node in document.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>()) node.Remove();
            text = HtmlAgilityPack.HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
            text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\n\\s*\\n\\s*\\n+", "\n\n");
            return Truncate(text.Trim(), 12000);
        }
        return string.Empty;
    }

    private sealed class PollState
    {
        public string UidValidity { get; set; } = string.Empty;
        public int LastUid { get; set; }
        public bool Initialized { get; set; }
        public bool ImportExisting { get; set; }
    }

    private string StatePath(MailboxOptions account)
    {
        var key = $"{account.Host}|{account.Port}|{(account.Secure ? "tls" : "starttls")}|{account.User}|{account.Mailbox}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..24];
        return Path.Combine(runtime.MailStateDirectory, $"{hash}.json");
    }

    private static PollState ReadState(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<PollState>(File.ReadAllText(path), JsonDefaults.Options);
            if (state is null) return new PollState();
            if (state.LastUid > 0) state.Initialized = true;
            return state;
        }
        catch (FileNotFoundException) { return new PollState(); }
        catch (DirectoryNotFoundException) { return new PollState(); }
        catch (JsonException) { return new PollState(); }
    }

    private static async Task SaveStateAsync(string path, PollState state, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonDefaults.Options), ct);
        File.Move(temporary, path, true);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static string CleanName(string? value) =>
        System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Replace('\u0000', ' '), "[\\u0000-\\u001f\\u007f]", " ").Trim();
}

