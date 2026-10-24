using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Mail;

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
                Provider = mailbox.Provider,
                Label = mailbox.Label,
                Host = mailbox.Host,
                Port = mailbox.Port,
                Secure = mailbox.Secure,
                User = mailbox.User,
                Password = string.Empty,
                Mailbox = mailbox.Mailbox,
                HasPassword = mailbox.Password.Length > 0,
                TenantId = mailbox.TenantId,
                ClientId = mailbox.ClientId,
                ClientSecret = string.Empty,
                HasClientSecret = mailbox.ClientSecret.Length > 0,
                RefreshToken = string.Empty,
                HasRefreshToken = mailbox.RefreshToken.Length > 0,
                ServiceAccountJson = string.Empty,
                HasServiceAccount = mailbox.ServiceAccountJson.Length > 0,
                HasCredentials = HasCredentials(mailbox)
            }).ToList()
        };
    }

    public async Task SaveAsync(MailOptions input, CancellationToken ct = default)
    {
        Sanitize(input);
        var previous = await GetAsync(ct);
        var byProviderAndUser = previous?.Mailboxes
            .GroupBy(mailbox => Identity(mailbox))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, MailboxOptions>(StringComparer.OrdinalIgnoreCase);

        foreach (var mailbox in input.Mailboxes)
        {
            if (previous?.Mailboxes.FirstOrDefault(item => item.Id == mailbox.Id) is { } existingById)
            {
                CopySecrets(existingById, mailbox);
            }
            else if (byProviderAndUser.TryGetValue(Identity(mailbox), out var existingByAddress))
            {
                CopySecrets(existingByAddress, mailbox);
            }
        }

        Validate(input);
        var duplicate = input.Mailboxes.GroupBy(mailbox => Identity(mailbox), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw ApiException.BadRequest($"{duplicate.First().User} aynı sağlayıcıda iki kez tanımlanmış.");
        var duplicateId = input.Mailboxes.GroupBy(mailbox => mailbox.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw ApiException.BadRequest("Mail hesabı kimliği iki kez kullanılmış.");
        await settings.SetAsync(OwnerId, "mail_config", input, ct);
    }

    public Task<MailPollStatus?> GetPollStatusAsync(CancellationToken ct = default) =>
        settings.GetAsync<MailPollStatus>(OwnerId, "mail_poll_status", ct);

    public Task SetPollStatusAsync(MailPollStatus status, CancellationToken ct = default) =>
        settings.SetAsync(OwnerId, "mail_poll_status", status, ct);

    private static MailOptions? Normalize(MailOptions? value)
    {
        if (value is null) return null;
        value.Mailboxes ??= new();
        value.PollSeconds = Math.Clamp(value.PollSeconds == 0 ? 60 : value.PollSeconds, 15, 3600);
        value.MaxPerPoll = Math.Clamp(value.MaxPerPoll == 0 ? 25 : value.MaxPerPoll, 1, 100);
        Sanitize(value);
        return value;
    }

    private static void Sanitize(MailOptions value)
    {
        value.Mailboxes ??= new();
        value.Mailboxes.RemoveAll(mailbox => mailbox is null);
        foreach (var mailbox in value.Mailboxes)
        {
            mailbox.Id = (mailbox.Id ?? string.Empty).Trim();
            mailbox.Label = (mailbox.Label ?? string.Empty).Trim();
            mailbox.Provider = NormalizeProvider(mailbox.Provider);
            mailbox.Host = (mailbox.Host ?? string.Empty).Trim();
            mailbox.User = (mailbox.User ?? string.Empty).Trim();
            mailbox.Mailbox = (mailbox.Mailbox ?? string.Empty).Trim();
            mailbox.Password ??= string.Empty;
            mailbox.TenantId = (mailbox.TenantId ?? string.Empty).Trim();
            mailbox.ClientId = (mailbox.ClientId ?? string.Empty).Trim();
            mailbox.ClientSecret ??= string.Empty;
            mailbox.RefreshToken ??= string.Empty;
            mailbox.ServiceAccountJson ??= string.Empty;
            ApplyProviderDefaults(mailbox);
        }
    }

    private static void ApplyProviderDefaults(MailboxOptions mailbox)
    {
        switch (mailbox.Provider)
        {
            case MailProviderKind.MicrosoftGraph:
                mailbox.Host = "graph.microsoft.com";
                mailbox.Port = 443;
                mailbox.Secure = true;
                mailbox.Mailbox = GraphMailProvider.NormalizeFolder(mailbox.Mailbox);
                break;
            case MailProviderKind.Gmail:
                mailbox.Host = "gmail.googleapis.com";
                mailbox.Port = 443;
                mailbox.Secure = true;
                if (mailbox.Mailbox.Length == 0) mailbox.Mailbox = "INBOX";
                else if (GmailLabels.TryNormalizeSystemLabelId(mailbox.Mailbox, out var labelId)) mailbox.Mailbox = labelId;
                break;
        }
    }

    private static MailProviderKind NormalizeProvider(MailProviderKind provider) => provider switch
    {
        MailProviderKind.MicrosoftGraph => MailProviderKind.MicrosoftGraph,
        MailProviderKind.Gmail => MailProviderKind.Gmail,
        _ => MailProviderKind.Imap
    };

    private static void Validate(MailOptions input)
    {
        var hostPattern = new System.Text.RegularExpressions.Regex("^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var idPattern = new System.Text.RegularExpressions.Regex("^[a-z0-9_-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (input.Mailboxes.Count is < 1 or > 10)
            throw ApiException.BadRequest(input.Mailboxes.Count == 0 ? "En az bir mail hesabı gerekli." : "En fazla 10 mail hesabı eklenebilir.");
        foreach (var mailbox in input.Mailboxes)
        {
            if (mailbox.Id.Length is < 1 or > 64 || !idPattern.IsMatch(mailbox.Id))
                throw ApiException.BadRequest("Mail hesabı kimliği geçersiz.");
            if (mailbox.Label.Length > 80) throw ApiException.BadRequest("Mail hesabı adı çok uzun.");

            switch (mailbox.Provider)
            {
                case MailProviderKind.MicrosoftGraph:
                    ValidateMailboxMailbox(mailbox);
                    ValidateUser(mailbox);
                    ValidateTenant(mailbox.TenantId);
                    ValidateClient(mailbox.ClientId, mailbox.ClientSecret);
                    break;
                case MailProviderKind.Gmail:
                    ValidateMailboxMailbox(mailbox);
                    ValidateUser(mailbox);
                    if (!mailbox.User.Contains('@') || mailbox.User.Any(char.IsWhiteSpace))
                        throw ApiException.BadRequest("Google Workspace kullanıcı adresi geçerli bir e-posta adresi olmalı.");
                    if (mailbox.ServiceAccountJson.Length > 40_000) throw ApiException.BadRequest("Google hizmet hesabı JSON dosyası çok büyük.");
                    if (mailbox.ServiceAccountJson.Length > 0) GoogleServiceAccountJson.Parse(mailbox.ServiceAccountJson);
                    else ValidateClient(mailbox.ClientId, mailbox.ClientSecret, mailbox.RefreshToken);
                    break;
                default:
                    if (mailbox.Host.Length is < 1 or > 253 || !hostPattern.IsMatch(mailbox.Host))
                        throw ApiException.BadRequest("IMAP sunucusu geçerli bir alan adı olmalı.");
                    if (mailbox.Port is < 1 or > 65535) throw ApiException.BadRequest("IMAP portu geçersiz.");
                    ValidateUser(mailbox);
                    ValidateMailboxMailbox(mailbox);
                    if (mailbox.Password.Length > 1024) throw ApiException.BadRequest("IMAP parolası çok uzun.");
                    break;
            }

            if (input.Enabled && !HasCredentials(mailbox))
                throw ApiException.BadRequest($"{mailbox.User} için {ProviderLabel(mailbox.Provider)} kimlik bilgileri gerekli.");
        }
        if (input.PollSeconds is < 15 or > 3600) throw ApiException.BadRequest("Kontrol aralığı 15-3600 saniye olmalı.");
        if (input.MaxPerPoll is < 1 or > 100) throw ApiException.BadRequest("Tek kontrolde en fazla 100 e-posta işlenebilir.");
    }

    private static void ValidateUser(MailboxOptions mailbox)
    {
        if (mailbox.User.Length is < 1 or > 254 || mailbox.User.Contains('\r') || mailbox.User.Contains('\n'))
            throw ApiException.BadRequest("Mail hesabı geçersiz.");
    }

    private static void ValidateMailboxMailbox(MailboxOptions mailbox)
    {
        if (mailbox.Mailbox.Length is < 1 or > 1024 || mailbox.Mailbox.Any(character => character is '\r' or '\n' || char.IsControl(character)))
            throw ApiException.BadRequest("Gelen kutusu adı geçersiz.");
    }

    private static void ValidateTenant(string tenantId)
    {
        if (tenantId.Length > 253 || tenantId.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            throw ApiException.BadRequest("Microsoft 365 kiracı kimliği veya alan adı geçersiz.");
    }

    private static void ValidateClient(string clientId, string clientSecret, string refreshToken = "")
    {
        if (clientId.Length > 200 || clientId.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            throw ApiException.BadRequest("OAuth istemci kimliği geçersiz.");
        if (clientSecret.Length > 2048)
            throw ApiException.BadRequest("OAuth istemci parolası çok uzun.");
        if (refreshToken.Length > 4096)
            throw ApiException.BadRequest("Google yenileme belirteci çok uzun.");
    }

    private static bool HasCredentials(MailboxOptions mailbox) => mailbox.Provider switch
    {
        MailProviderKind.MicrosoftGraph => mailbox.TenantId.Length > 0 && mailbox.ClientId.Length > 0 && mailbox.ClientSecret.Length > 0,
        MailProviderKind.Gmail => mailbox.ServiceAccountJson.Length > 0 ||
            (mailbox.ClientId.Length > 0 && mailbox.ClientSecret.Length > 0 && mailbox.RefreshToken.Length > 0),
        _ => mailbox.Password.Length > 0
    };

    private static string ProviderLabel(MailProviderKind provider) => provider switch
    {
        MailProviderKind.MicrosoftGraph => "Microsoft Graph",
        MailProviderKind.Gmail => "Google Workspace",
        _ => "IMAP"
    };

    private static string Identity(MailboxOptions mailbox) =>
        $"{mailbox.Provider}|{mailbox.User.ToLowerInvariant()}|{mailbox.Host.ToLowerInvariant()}";

    private static void CopySecrets(MailboxOptions from, MailboxOptions to)
    {
        if (from.Provider != to.Provider) return;
        if (string.IsNullOrEmpty(to.Password)) to.Password = from.Password;
        if (string.IsNullOrEmpty(to.ClientSecret)) to.ClientSecret = from.ClientSecret;
        if (string.IsNullOrEmpty(to.RefreshToken)) to.RefreshToken = from.RefreshToken;
        if (string.IsNullOrEmpty(to.ServiceAccountJson)) to.ServiceAccountJson = from.ServiceAccountJson;
    }
}

internal static class ObjectExtensions
{
    public static void Let<T>(this T value, Action<T> action) => action(value);
}
