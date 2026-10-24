using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickeTask.Application.Abstractions;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

public sealed class MailPoller : BackgroundService
{
    private const int TickIntervalMilliseconds = 5_000;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyDictionary<MailProviderKind, IMailProvider> _providers;
    private readonly ILogger<MailPoller> _logger;
    private readonly MailPollSchedule _schedule = new();
    private bool _idle;

    public MailPoller(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IMailProvider> providers,
        ILogger<MailPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _providers = providers.ToDictionary(provider => provider.Provider);
        _logger = logger;
    }

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
                _logger.LogError(exception, "Mail poller failed");
            }
            try { await Task.Delay(TickIntervalMilliseconds, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IMailConfigService>();
        var settings = await config.GetAsync(ct);
        if (settings is null || !settings.Enabled || settings.Mailboxes.Count == 0)
        {
            _schedule.Reset();
            if (!_idle)
            {
                _idle = true;
                await config.SetPollStatusAsync(new MailPollStatus
                {
                    State = MailCheckState.Idle,
                    CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Message = "Mail kanalı kapalı veya yapılandırılmadı.",
                    Mailboxes = new Dictionary<string, MailboxPollStatus>()
                }, ct);
            }
            return;
        }

        _idle = false;
        var now = DateTimeOffset.UtcNow;
        var fingerprint = Fingerprint(settings);
        if (!_schedule.ShouldPoll(now, fingerprint)) return;

        _schedule.MarkPolled(now, TimeSpan.FromSeconds(settings.PollSeconds));
        var ingestion = scope.ServiceProvider.GetRequiredService<IMailIngestionService>();
        var results = new Dictionary<string, MailboxPollStatus>();
        foreach (var mailbox in settings.Mailboxes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!_providers.TryGetValue(mailbox.Provider, out var provider))
                    throw new InvalidOperationException($"{mailbox.Provider} mail sağlayıcısı kayıtlı değil.");
                results[mailbox.Id] = await provider.PollAsync(mailbox, settings, ingestion, ct);
            }
            catch (Exception exception)
            {
                var message = Truncate(exception.Message, 500);
                results[mailbox.Id] = new MailboxPollStatus
                {
                    State = MailCheckState.Error,
                    CheckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Message = message
                };
                _logger.LogError(exception, "Mail account poll failed: {Account} ({Provider})", mailbox.Id, mailbox.Provider);
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
        catch (Exception exception) { _logger.LogWarning(exception, "Mail retry queue failed"); }
    }

    private static string Fingerprint(MailOptions settings)
    {
        var builder = new StringBuilder(512);
        builder.Append(settings.Enabled).Append('|')
            .Append(settings.DryRun).Append('|')
            .Append(settings.PollSeconds).Append('|')
            .Append(settings.ImportExisting).Append('|')
            .Append(settings.MaxPerPoll);
        foreach (var mailbox in settings.Mailboxes)
        {
            builder.Append('\n')
                .Append(mailbox.Id).Append('\u001f')
                .Append(mailbox.Provider).Append('\u001f')
                .Append(mailbox.Label).Append('\u001f')
                .Append(mailbox.Host).Append('\u001f')
                .Append(mailbox.Port).Append('\u001f')
                .Append(mailbox.Secure).Append('\u001f')
                .Append(mailbox.User).Append('\u001f')
                .Append(mailbox.Mailbox).Append('\u001f')
                .Append(mailbox.TenantId).Append('\u001f')
                .Append(mailbox.ClientId).Append('\u001f')
                .Append(mailbox.Password).Append('\u001f')
                .Append(mailbox.ClientSecret).Append('\u001f')
                .Append(mailbox.RefreshToken).Append('\u001f')
                .Append(mailbox.ServiceAccountJson);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

internal sealed class MailPollSchedule
{
    private string? _fingerprint;
    private DateTimeOffset? _nextPollAt;

    public bool ShouldPoll(DateTimeOffset now, string fingerprint)
    {
        if (!string.Equals(_fingerprint, fingerprint, StringComparison.Ordinal))
        {
            _fingerprint = fingerprint;
            _nextPollAt = now;
        }
        return _nextPollAt is null || now >= _nextPollAt.Value;
    }

    public void MarkPolled(DateTimeOffset now, TimeSpan interval) => _nextPollAt = now + interval;

    public void Reset()
    {
        _fingerprint = null;
        _nextPollAt = null;
    }
}
