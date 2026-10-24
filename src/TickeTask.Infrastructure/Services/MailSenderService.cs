using System.Text.RegularExpressions;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Domain.Entities;

namespace TickeTask.Infrastructure.Services;

public sealed class MailSenderService(
    IMailSenderRepository repository,
    ISettingRepository settings,
    AppRuntime runtime,
    IClock clock,
    IIdGenerator ids) : IMailSenderService
{
    private const string OwnerId = "local-user";
    private static readonly Regex DomainPattern = new("^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<List<MailSenderInfo>> ListAsync(CancellationToken ct = default)
    {
        var env = runtime.SenderEmails();
        var rows = await repository.ListAsync(ct);
        var byEmail = new Dictionary<string, MailSenderInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in env)
            byEmail[email] = new MailSenderInfo(null, email, string.Empty, true, true);
        foreach (var row in rows)
            byEmail[row.Email] = new MailSenderInfo(row.Id, row.Email, row.Name, row.Active, env.Contains(row.Email));
        return byEmail.Values.OrderBy(sender => sender.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<MailDomainInfo>> ListDomainsAsync(CancellationToken ct = default)
    {
        var env = runtime.SenderDomains();
        var saved = await SavedDomainsAsync(ct);
        return env.Concat(saved).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .Select(domain => new MailDomainInfo(domain, env.Contains(domain))).ToList();
    }

    public async Task AddDomainAsync(string domain, CancellationToken ct = default)
    {
        var normalized = NormalizeDomain(domain);
        if (!DomainPattern.IsMatch(normalized))
            throw ApiException.BadRequest("Geçerli bir alan adı girin. Örnek: sirket.com");
        var saved = await SavedDomainsAsync(ct);
        if (!saved.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            await settings.SetAsync(OwnerId, "mail_domains", saved.Append(normalized).ToList(), ct);
    }

    public async Task RemoveDomainAsync(string domain, CancellationToken ct = default)
    {
        var normalized = NormalizeDomain(domain);
        if (runtime.SenderDomains().Contains(normalized))
            throw ApiException.BadRequest("Bu alan adı .env dosyasındaki ALLOWED_DOMAINS listesinde tanımlı; buradan kaldırılamaz.");
        var saved = await SavedDomainsAsync(ct);
        await settings.SetAsync(OwnerId, "mail_domains", saved.Where(item => !item.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList(), ct);
    }

    public async Task<AllowedSender?> GetAllowedAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var parts = TickeTask.Application.Mail.MailTriage.SplitEmail(normalized);
        if (parts is null) return null;
        var row = await repository.GetAsync(normalized, ct);
        if (row is not null) return row.Active ? new AllowedSender(row.Email, row.Name) : null;
        if (runtime.SenderEmails().Contains(normalized)) return new AllowedSender(normalized, string.Empty);
        var domains = runtime.SenderDomains().Concat(await SavedDomainsAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return domains.Contains(parts.Value.Domain) ? new AllowedSender(normalized, string.Empty) : null;
    }

    public async Task SaveAsync(string email, string name, bool active, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalized, "^[^@\\s]+@[^@\\s]+$")) throw ApiException.BadRequest("Geçerli bir e-posta adresi girin.");
        var now = clock.UnixNow;
        var existing = await repository.GetAsync(normalized, ct);
        await repository.UpsertAsync(new MailSender
        {
            Id = existing?.Id ?? ids.Create("snd"),
            Email = normalized,
            Name = name.Trim(),
            Active = active,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        }, ct);
    }

    private async Task<List<string>> SavedDomainsAsync(CancellationToken ct)
    {
        var saved = await settings.GetAsync<List<string>>(OwnerId, "mail_domains", ct) ?? new List<string>();
        return saved.Select(NormalizeDomain).Where(domain => DomainPattern.IsMatch(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeDomain(string value) => (value ?? string.Empty).Trim().ToLowerInvariant().TrimStart('@');
}

