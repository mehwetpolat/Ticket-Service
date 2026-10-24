using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Infrastructure.Persistence;
using TickeTask.Infrastructure.Security;

namespace TickeTask.Infrastructure.Persistence.Repositories;

public sealed class SettingRepository(AppDbContext db, IEncryptionService encryption) : ISettingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonDefaults.Options)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T?> GetAsync<T>(string ownerId, string key, CancellationToken ct = default)
    {
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(setting => setting.OwnerId == ownerId && setting.Key == key, ct);
        if (row is null) return default;
        var clear = encryption.Decrypt(row.Value);
        return JsonSerializer.Deserialize<T>(clear, JsonOptions);
    }

    public async Task SetAsync<T>(string ownerId, string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var encrypted = encryption.Encrypt(json);
        var row = await db.AppSettings.FirstOrDefaultAsync(setting => setting.OwnerId == ownerId && setting.Key == key, ct);
        if (row is null)
        {
            await db.AppSettings.AddAsync(new AppSetting
            {
                OwnerId = ownerId,
                Key = key,
                Value = encrypted,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }, ct);
        }
        else
        {
            row.Value = encrypted;
            row.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class MailSenderRepository(AppDbContext db) : IMailSenderRepository
{
    public Task<List<MailSender>> ListAsync(CancellationToken ct = default) =>
        db.MailSenders.AsNoTracking().OrderBy(sender => sender.Name).ThenBy(sender => sender.Email).ToListAsync(ct);

    public Task<MailSender?> GetAsync(string email, CancellationToken ct = default) =>
        db.MailSenders.FirstOrDefaultAsync(sender => sender.Email == email, ct);

    public async Task UpsertAsync(MailSender sender, CancellationToken ct = default)
    {
        var existing = await db.MailSenders.FirstOrDefaultAsync(row => row.Email == sender.Email, ct);
        if (existing is null)
        {
            await db.MailSenders.AddAsync(sender, ct);
        }
        else
        {
            existing.Name = sender.Name;
            existing.Active = sender.Active;
            existing.UpdatedAt = sender.UpdatedAt;
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class MailIngestionRepository(AppDbContext db) : IMailIngestionRepository
{
    public Task<MailIngestion?> FindDuplicateAsync(string ownerId, string messageId, CancellationToken ct = default) =>
        db.MailIngestions.AsNoTracking()
            .Where(ingestion => ingestion.OwnerId == ownerId && ingestion.MessageId == messageId)
            .OrderBy(ingestion => ingestion.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<MailIngestion?> FindIdentityAsync(string ownerId, string mailbox, string messageId, CancellationToken ct = default) =>
        db.MailIngestions.FirstOrDefaultAsync(ingestion =>
            ingestion.OwnerId == ownerId && ingestion.Mailbox == mailbox && ingestion.MessageId == messageId, ct);

    public Task<MailIngestion?> GetAsync(string id, CancellationToken ct = default) =>
        db.MailIngestions.FirstOrDefaultAsync(ingestion => ingestion.Id == id, ct);

    public async Task<List<MailIngestion>> ListRetryableAsync(long leaseThreshold, long nowUnix, CancellationToken ct = default) =>
        await db.MailIngestions.AsNoTracking()
            .Where(ingestion => ingestion.Status == MailIngestionStatus.Failed ||
                (ingestion.Status == MailIngestionStatus.Processing && ingestion.UpdatedAt < leaseThreshold))
            .OrderBy(ingestion => ingestion.UpdatedAt).Take(100).ToListAsync(ct);

    public async Task<bool> ClaimAsync(string id, long nowUnix, long leaseThreshold, CancellationToken ct = default)
    {
        var affected = await db.MailIngestions
            .Where(ingestion => ingestion.Id == id &&
                (ingestion.Status == MailIngestionStatus.Pending || ingestion.Status == MailIngestionStatus.Failed ||
                 (ingestion.Status == MailIngestionStatus.Processing && ingestion.UpdatedAt < leaseThreshold)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(ingestion => ingestion.Status, MailIngestionStatus.Processing)
                .SetProperty(ingestion => ingestion.Attempts, ingestion => ingestion.Attempts + 1)
                .SetProperty(ingestion => ingestion.UpdatedAt, nowUnix), ct);
        return affected > 0;
    }

    public async Task AddAsync(MailIngestion ingestion, CancellationToken ct = default)
    {
        await db.MailIngestions.AddAsync(ingestion, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<MailIngestionView>> ListAsync(int limit, CancellationToken ct = default) =>
        await db.MailIngestions.AsNoTracking()
            .OrderByDescending(ingestion => ingestion.CreatedAt).Take(limit)
            .Select(ingestion => new MailIngestionView(
                ingestion.Id, ingestion.Mailbox, ingestion.MessageId, ingestion.SenderEmail, ingestion.SenderName,
                ingestion.Subject, ingestion.Status == MailIngestionStatus.DryRun ? "dry_run" : ingestion.Status.ToString().ToLowerInvariant(),
                ingestion.Attempts, ingestion.Error, ingestion.TicketId, ingestion.CreatedAt, ingestion.UpdatedAt,
                ingestion.Ticket != null ? ingestion.Ticket.ExternalId : null,
                ingestion.Ticket != null ? ingestion.Ticket.ExternalUrl : null,
                ingestion.Ticket != null ? LowerTarget(ingestion.Ticket.Target) : null))
            .ToListAsync(ct);

    public async Task<Dictionary<string, int>> StatsSinceAsync(long sinceUnix, CancellationToken ct = default)
    {
        var rows = await db.MailIngestions.AsNoTracking().Where(ingestion => ingestion.CreatedAt > sinceUnix)
            .GroupBy(ingestion => ingestion.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        return rows.ToDictionary(row => row.Status == MailIngestionStatus.DryRun ? "dry_run" : row.Status.ToString().ToLowerInvariant(), row => row.Count);
    }

    public async Task<Dictionary<string, int>> StatsAsync(CancellationToken ct = default)
    {
        var rows = await db.MailIngestions.AsNoTracking().GroupBy(ingestion => ingestion.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        return rows.ToDictionary(row => row.Status == MailIngestionStatus.DryRun ? "dry_run" : row.Status.ToString().ToLowerInvariant(), row => row.Count);
    }

    private static string LowerTarget(TicketTarget target) => target.ToString().ToLowerInvariant();
}

