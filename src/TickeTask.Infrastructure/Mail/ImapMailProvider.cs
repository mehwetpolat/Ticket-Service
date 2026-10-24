using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

public sealed class ImapMailProvider(
    AppRuntime runtime,
    ILogger<ImapMailProvider> logger) : IMailProvider
{
    public MailProviderKind Provider => MailProviderKind.Imap;

    public async Task<MailboxPollStatus> PollAsync(MailboxOptions account, MailOptions options, IMailIngestionService ingestion, CancellationToken ct = default)
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
                account.Secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
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
            var statePath = MailStateFile.Path(runtime, account);
            var uidValidity = folder.UidValidity.ToString();
            var uidNext = folder.UidNext?.Id ?? 0;
            var state = MailStateFile.Read<PollState>(statePath);
            if (state.LastUid > 0) state.Initialized = true;

            if (state.Initialized && options.ImportExisting != state.ImportExisting)
            {
                state.LastUid = options.ImportExisting ? 0 : Math.Max(0, (int)uidNext - 1);
                state.ImportExisting = options.ImportExisting;
                await MailStateFile.WriteAsync(statePath, state, ct);
            }

            if (!state.Initialized)
            {
                state.UidValidity = uidValidity;
                state.Initialized = true;
                state.ImportExisting = options.ImportExisting;
                state.LastUid = options.ImportExisting ? 0 : Math.Max(0, (int)uidNext - 1);
                await MailStateFile.WriteAsync(statePath, state, ct);
                if (!options.ImportExisting)
                    return Status(MailCheckState.Connected, "Bağlantı başarılı. Yalnızca bundan sonra gelen e-postalar işlenecek.", state.LastUid);
            }

            if (state.UidValidity != uidValidity)
            {
                state.UidValidity = uidValidity;
                state.LastUid = options.ImportExisting ? 0 : Math.Max(0, (int)uidNext - 1);
                state.Initialized = true;
                state.ImportExisting = options.ImportExisting;
                await MailStateFile.WriteAsync(statePath, state, ct);
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
                    var sender = MailMessageText.SingleSender(message);
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
                            SenderName = MailMessageText.CleanName(sender.Name),
                            Subject = Clip(MailMessageText.CleanName(message.Subject), 500),
                            Body = MailMessageText.PlainBody(message),
                            AttachmentNames = message.Attachments
                                .Select(attachment => MailMessageText.CleanName(attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name))
                                .Where(name => name.Length > 0).Take(30).ToList(),
                            AutoSubmitted = MailMessageText.AutoSubmitted(message),
                            Bulk = MailMessageText.Bulk(message),
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
                await MailStateFile.WriteAsync(statePath, state, ct);
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

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed class PollState
    {
        public string UidValidity { get; set; } = string.Empty;
        public int LastUid { get; set; }
        public bool Initialized { get; set; }
        public bool ImportExisting { get; set; }
    }
}
