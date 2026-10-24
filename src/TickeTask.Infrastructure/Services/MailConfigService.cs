using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Services;

public sealed class MailConfigService(ISettingRepository settings) : IMailConfigService
{
    private const string OwnerId = "local-user";

    public async Task<MailOptions?> GetAsync(CancellationToken ct = default) =>
        Normalize(await settings.GetAsync<MailOptions>(OwnerId, "mail_config", ct));

    public async Task<AdminMailOptions?> GetForAdminAsync(CancellationToken ct = default)
    {
        var config = await GetAsync(ct);
        if (config is null) return null;
        return new AdminMailOptions
        {
            Enabled = config.Enabled,
            DryRun = config.DryRun,
            PollSeconds = config.PollSeconds,
            ImportExisting = config.ImportExisting,
            MaxPerPoll = config.MaxPerPoll,
            Mailboxes = config.Mailboxes.Select(mailbox => new AdminMailbox
            {
                Id = mailbox.Id,
                Label = mailbox.Label,
                Host = mailbox.Host,
                Port = mailbox.Port,
                Secure = mailbox.Secure,
                User = mailbox.User,
                Password = string.Empty,
                Mailbox = mailbox.Mailbox,
                HasPassword = mailbox.Password.Length > 0
            }).ToList()
        };
    }

    public async Task SaveAsync(MailOptions input, CancellationToken ct = default)
    {
        Validate(input);
        var previous = await GetAsync(ct);
        var byAddress = previous?.Mailboxes.ToDictionary(mailbox => mailbox.User.ToLowerInvariant()) ?? new Dictionary<string, MailboxOptions>();
        foreach (var mailbox in input.Mailboxes)
        {
            previous?.Mailboxes.FirstOrDefault(item => item.Id == mailbox.Id)?.Let(existing => mailbox.Password = mailbox.Password.Length > 0 ? mailbox.Password : existing.Password);
            if (mailbox.Password.Length == 0 && byAddress.TryGetValue(mailbox.User.ToLowerInvariant(), out var existingByAddress))
                mailbox.Password = existingByAddress.Password;
            if (input.Enabled && mailbox.Password.Length == 0)
                throw ApiException.BadRequest($"{mailbox.User} için IMAP parolası gerekli.");
        }
        var duplicate = input.Mailboxes.GroupBy(mailbox => mailbox.User.ToLowerInvariant()).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw ApiException.BadRequest($"{duplicate.First().User} aynı ayarda iki kez tanımlanmış.");
        await settings.SetAsync(OwnerId, "mail_config", input, ct);
    }

    public Task<MailPollStatus?> GetPollStatusAsync(CancellationToken ct = default) =>
        settings.GetAsync<MailPollStatus>(OwnerId, "mail_poll_status", ct);

    public Task SetPollStatusAsync(MailPollStatus status, CancellationToken ct = default) =>
        settings.SetAsync(OwnerId, "mail_poll_status", status, ct);

    private static MailOptions? Normalize(MailOptions? value)
    {
        if (value is null) return null;
        value.PollSeconds = Math.Clamp(value.PollSeconds == 0 ? 60 : value.PollSeconds, 15, 3600);
        value.MaxPerPoll = Math.Clamp(value.MaxPerPoll == 0 ? 25 : value.MaxPerPoll, 1, 100);
        return value;
    }

    private static void Validate(MailOptions input)
    {
        var hostPattern = new System.Text.RegularExpressions.Regex("^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var idPattern = new System.Text.RegularExpressions.Regex("^[a-z0-9_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (input.Mailboxes.Count is < 1 or > 10)
            throw ApiException.BadRequest(input.Mailboxes.Count == 0 ? "En az bir mail hesabı gerekli." : "En fazla 10 mail hesabı eklenebilir.");
        foreach (var mailbox in input.Mailboxes)
        {
            mailbox.Id = (mailbox.Id ?? string.Empty).Trim();
            mailbox.Label = (mailbox.Label ?? string.Empty).Trim();
            mailbox.Host = (mailbox.Host ?? string.Empty).Trim();
            mailbox.User = (mailbox.User ?? string.Empty).Trim();
            mailbox.Mailbox = (mailbox.Mailbox ?? string.Empty).Trim();
            mailbox.Password ??= string.Empty;
            if (mailbox.Id.Length is < 1 or > 64 || !idPattern.IsMatch(mailbox.Id))
                throw ApiException.BadRequest("Mail hesabı kimliği geçersiz.");
            if (mailbox.Label.Length > 80) throw ApiException.BadRequest("Mail hesabı adı çok uzun.");
            if (mailbox.Host.Length is < 1 or > 253 || !hostPattern.IsMatch(mailbox.Host))
                throw ApiException.BadRequest("IMAP sunucusu geçerli bir alan adı olmalı.");
            if (mailbox.Port is < 1 or > 65535) throw ApiException.BadRequest("IMAP portu geçersiz.");
            if (mailbox.User.Length is < 1 or > 254 || mailbox.User.Contains('\r') || mailbox.User.Contains('\n'))
                throw ApiException.BadRequest("Mail hesabı geçersiz.");
            if (mailbox.Password.Length > 1024) throw ApiException.BadRequest("IMAP parolası çok uzun.");
            if (mailbox.Mailbox.Length is < 1 or > 180 || mailbox.Mailbox.Any(character => character is '\r' or '\n' || char.IsControl(character)))
                throw ApiException.BadRequest("Gelen kutusu adı geçersiz.");
        }
        if (input.PollSeconds is < 15 or > 3600) throw ApiException.BadRequest("Kontrol aralığı 15-3600 saniye olmalı.");
        if (input.MaxPerPoll is < 1 or > 100) throw ApiException.BadRequest("Tek kontrolde en fazla 100 e-posta işlenebilir.");
    }
}

internal static class ObjectExtensions
{
    public static void Let<T>(this T value, Action<T> action) => action(value);
}
