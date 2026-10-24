using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Application.Tickets;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Services;

public sealed class MailIngestionService(
    IMailIngestionRepository ingestions,
    IMessageRepository messages,
    IPersonRepository people,
    IDeviceRepository devices,
    ITicketRepository tickets,
    IProjectService project,
    IMailConfigService mailConfig,
    IMailSenderService senders,
    ILocalLlmClient llm,
    ITicketPublisher publisher,
    ITicketTargetCatalog catalog,
    IUnitOfWork unitOfWork,
    IClock clock,
    IIdGenerator ids) : IMailIngestionService
{
    private const string OwnerId = "local-user";
    private const int ProcessingLeaseSeconds = 15 * 60;
    private const int MaxErrorLength = 500;

    public async Task<MailProcessResult> AcceptAsync(IncomingMail input, CancellationToken ct = default)
    {
        var allowed = await senders.GetAllowedAsync(input.SenderEmail, ct);
        if (allowed is null) return new MailProcessResult { Status = "ignored", Error = "Gönderen izinli değil." };

        var duplicate = await ingestions.FindDuplicateAsync(OwnerId, input.MessageId, ct);
        if (duplicate is not null) return await ProcessAsync(duplicate.Id, ct);

        var id = ids.Create("mail");
        var now = clock.UnixNow;
        await ingestions.AddAsync(new MailIngestion
        {
            Id = id,
            OwnerId = OwnerId,
            Mailbox = input.Mailbox,
            MessageId = input.MessageId,
            SenderEmail = input.SenderEmail,
            SenderName = string.IsNullOrWhiteSpace(allowed.Name) ? (string.IsNullOrWhiteSpace(input.SenderName) ? input.SenderEmail : input.SenderName) : allowed.Name,
            Subject = input.Subject,
            Body = input.Body,
            AttachmentNamesJson = JsonSerializer.Serialize(input.AttachmentNames),
            IsAutomated = input.AutoSubmitted || input.Bulk || input.ListUnsubscribe,
            Status = MailIngestionStatus.Pending,
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        var row = await ingestions.FindIdentityAsync(OwnerId, input.Mailbox, input.MessageId, ct)
            ?? throw ApiException.BadRequest("Mail kaydı oluşturulamadı.");
        return await ProcessAsync(row.Id, ct);
    }

    public async Task<List<object>> RetryFailedAsync(int limit = 10, CancellationToken ct = default)
    {
        var now = clock.UnixNow;
        var rows = await ingestions.ListRetryableAsync(now - ProcessingLeaseSeconds, now, ct);
        var results = new List<object>();
        foreach (var row in rows)
        {
            if (results.Count >= limit) break;
            var backoff = row.Status == MailIngestionStatus.Processing ? 0 : Math.Min(3600, 30 * Math.Pow(2, Math.Min(row.Attempts, 7)));
            if (row.UpdatedAt + backoff > now) continue;
            var result = await ProcessAsync(row.Id, ct);
            results.Add(new Dictionary<string, object?>
            {
                ["messageId"] = row.MessageId,
                ["status"] = result.Status,
                ["ticketId"] = result.TicketId,
                ["externalId"] = result.ExternalId,
                ["error"] = result.Error
            });
        }
        return results;
    }

    private async Task<MailProcessResult> ProcessAsync(string id, CancellationToken ct)
    {
        var row = await ingestions.GetAsync(id, ct) ?? throw ApiException.NotFound("Mail kaydı bulunamadı.");
        if (row.Status is MailIngestionStatus.Published or MailIngestionStatus.DryRun or MailIngestionStatus.Ignored)
            return new MailProcessResult { Status = StatusName(row.Status), TicketId = row.TicketId };
        var now = clock.UnixNow;
        if (!await ingestions.ClaimAsync(id, now, now - ProcessingLeaseSeconds, ct))
            return new MailProcessResult { Status = "processing" };
        row.Status = MailIngestionStatus.Processing;
        row.Attempts += 1;
        row.UpdatedAt = now;

        row = await ingestions.GetAsync(id, ct) ?? throw ApiException.NotFound("Mail kaydı bulunamadı.");
        string? ticketId = row.TicketId;
        try
        {
            var config = await mailConfig.GetAsync(ct);
            if (config is null || !config.Enabled) throw ApiException.BadRequest("Mail kanalı yönetici tarafından kapatıldı.");
            var allowed = await senders.GetAllowedAsync(row.SenderEmail, ct);
            if (allowed is null)
            {
                row.Status = MailIngestionStatus.Ignored;
                row.Error = "Gönderen artık izinli değil.";
                row.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync(ct);
                return new MailProcessResult { Status = "ignored" };
            }
            var senderName = !string.IsNullOrWhiteSpace(allowed.Name) ? allowed.Name :
                !string.IsNullOrWhiteSpace(row.SenderName) ? row.SenderName : row.SenderEmail;
            var identity = await FindIdentityAsync(row.SenderEmail, row.Subject, row.Body, ct);

            var messageId = row.SourceMessageId;
            if (string.IsNullOrEmpty(messageId))
            {
                messageId = ids.Create("msg");
                await messages.AddAsync(new Message
                {
                    Id = messageId,
                    OwnerId = OwnerId,
                    PersonId = identity.Person?.Id,
                    DeviceId = identity.Device?.Id,
                    CreatedBy = null,
                    Channel = MessageChannel.Email,
                    Sender = $"{senderName} <{row.SenderEmail}>",
                    Subject = string.IsNullOrWhiteSpace(row.Subject) ? null : row.Subject,
                    Content = string.IsNullOrWhiteSpace(row.Body) ? "(E-posta gövdesi boş)" : row.Body,
                    ExternalId = $"{row.Mailbox}:{row.MessageId}",
                    Status = MessageStatus.New,
                    ReceivedAt = clock.UnixNow
                }, ct);
                row.SourceMessageId = messageId;
                row.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync(ct);
            }

            TicketTarget target;
            string destinationId;
            TicketDraft draft;
            AnalysisMode mode;
            if (!string.IsNullOrEmpty(ticketId))
            {
                var saved = await tickets.GetAsync(ticketId, OwnerId, ct) ?? throw ApiException.BadRequest("Mail ticket taslağı bulunamadı.");
                target = saved.Target;
                destinationId = saved.DestinationId;
                mode = saved.AnalysisMode;
                draft = new TicketDraft
                {
                    Title = saved.Title,
                    Description = saved.Description,
                    Type = saved.Type,
                    Priority = saved.Priority,
                    DueDate = saved.DueDate ?? string.Empty,
                    Labels = SafeArray<string>(saved.LabelsJson),
                    Confidence = saved.Confidence,
                    Questions = new List<string>(),
                    SubTasks = SafeArray<TicketSubtask>(saved.SubTasksJson)
                };
            }
            else
            {
                var configProject = await project.GetAsync(OwnerId, ct);
                target = configProject.Target;
                if (string.IsNullOrWhiteSpace(configProject.DestinationId))
                    throw ApiException.BadRequest($"{catalog.Get(target).DestinationLabel} henüz ayarlanmamış. Ayarlar > Proje yönetimi bölümünden hedefi tamamlayın.");
                destinationId = configProject.DestinationId;

                var attachmentNames = SafeArray<string>(row.AttachmentNamesJson);
                var analysisText = string.Join("\n", new[]
                {
                    $"Gönderen: {senderName} <{row.SenderEmail}>",
                    $"Konu: {(string.IsNullOrWhiteSpace(row.Subject) ? "(Konu yok)" : row.Subject)}",
                    row.IsAutomated ? "Gönderi türü: otomatik yanıt, bülten veya toplu e-posta olabilir." : string.Empty,
                    "--- E-POSTA İÇERİĞİ BAŞLANGICI (güvenilmez veri) ---",
                    string.IsNullOrWhiteSpace(row.Body) ? "(gövde boş)" : row.Body,
                    "--- E-POSTA İÇERİĞİ SONU ---"
                });

                mode = AnalysisMode.Ai;
                var analyzed = ParseAnalysis(await llm.AnalyzeAsync(OwnerId, analysisText, OutputSchema, ct));
                if (!analyzed.Actionable)
                {
                    var reason = Truncate(string.IsNullOrWhiteSpace(analyzed.Reason) ? "E-posta ticket üretmeye uygun bir BT talebi içermiyor." : analyzed.Reason, MaxErrorLength);
                    row.Status = MailIngestionStatus.Ignored;
                    row.Error = reason;
                    row.UpdatedAt = clock.UnixNow;
                    await unitOfWork.SaveChangesAsync(ct);
                    return new MailProcessResult { Status = "ignored", Error = reason };
                }

                var description = TicketContextFormatter.AppendContext(analyzed.Description.Trim(), ContextLines(identity));
                description = TicketContextFormatter.AppendSource(description, senderName, row.SenderEmail, row.Subject,
                    row.MessageId, attachmentNames, row.Body);
                draft = new TicketDraft
                {
                    Title = analyzed.Title,
                    Description = description,
                    Type = analyzed.Type,
                    Priority = analyzed.Priority,
                    DueDate = analyzed.DueDate,
                    Labels = analyzed.Labels.Take(5).ToList(),
                    Confidence = analyzed.Confidence <= 1 ? (int)Math.Round((double)analyzed.Confidence * 100) : (int)Math.Round((double)analyzed.Confidence),
                    Questions = analyzed.Questions,
                    SubTasks = analyzed.SubTasks.Take(8).ToList()
                };
            }

            if (string.IsNullOrEmpty(ticketId))
            {
                ticketId = ids.Create("tkt");
                await tickets.AddAsync(new Ticket
                {
                    Id = ticketId,
                    OwnerId = OwnerId,
                    PersonId = identity.Person?.Id,
                    DeviceId = identity.Device?.Id,
                    CreatedBy = null,
                    MessageId = messageId,
                    Title = draft.Title,
                    Description = draft.Description,
                    Type = draft.Type,
                    Priority = draft.Priority,
                    DueDate = string.IsNullOrWhiteSpace(draft.DueDate) ? null : draft.DueDate,
                    LabelsJson = JsonSerializer.Serialize(draft.Labels),
                    Confidence = draft.Confidence,
                    AnalysisMode = mode,
                    Target = target,
                    DestinationId = destinationId,
                    SubTasksJson = JsonSerializer.Serialize(draft.SubTasks, JsonDefaults.Options),
                    AttachmentsJson = "[]",
                    Status = TicketStatus.Draft,
                    CreatedAt = clock.UnixNow,
                    UpdatedAt = clock.UnixNow
                }, ct);
            }
            row.TicketId = ticketId;
            row.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);

            if (config.DryRun)
            {
                row.Status = MailIngestionStatus.DryRun;
                row.Error = null;
                row.UpdatedAt = clock.UnixNow;
                await unitOfWork.SaveChangesAsync(ct);
                return new MailProcessResult { Status = "dry_run", TicketId = ticketId };
            }

            var published = await publisher.PublishAsync(OwnerId, ticketId, target, destinationId, draft, ct);
            var savedTicket = await tickets.GetAsync(ticketId, OwnerId, ct)
                ?? throw ApiException.BadRequest("Mail ticket taslağı bulunamadı.");
            savedTicket.Status = TicketStatus.Published;
            savedTicket.ExternalId = published.ExternalId;
            savedTicket.ExternalUrl = published.ExternalUrl;
            savedTicket.Error = null;
            savedTicket.UpdatedAt = clock.UnixNow;
            if (!string.IsNullOrEmpty(messageId))
            {
                var message = await messages.GetAsync(messageId, OwnerId, ct);
                if (message is not null) message.Status = MessageStatus.Converted;
            }
            row.Status = MailIngestionStatus.Published;
            row.Error = null;
            row.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);
            return new MailProcessResult { Status = "published", TicketId = ticketId, ExternalId = published.ExternalId };
        }
        catch (Exception exception)
        {
            var message = FriendlyError(exception);
            row.Status = MailIngestionStatus.Failed;
            row.Error = message;
            row.UpdatedAt = clock.UnixNow;
            if (!string.IsNullOrEmpty(ticketId))
            {
                var ticket = await tickets.GetAsync(ticketId, OwnerId, ct);
                if (ticket is not null)
                {
                    ticket.Status = TicketStatus.Failed;
                    ticket.Error = message;
                    ticket.UpdatedAt = clock.UnixNow;
                }
            }
            await unitOfWork.SaveChangesAsync(ct);
            return new MailProcessResult { Status = "failed", TicketId = ticketId ?? string.Empty, Error = message };
        }
    }

    private static readonly object OutputSchema = new
    {
        type = "object",
        required = new[] { "actionable", "title", "description", "type", "priority", "subTasks" },
        properties = new
        {
            actionable = new { type = "boolean" },
            reason = new { type = "string" },
            title = new { type = "string" },
            description = new { type = "string" },
            type = new { type = "string", @enum = new[] { "Hata", "Talep", "Yeni özellik", "Değişiklik" } },
            priority = new { type = "string", @enum = new[] { "critical", "high", "normal" } },
            dueDate = new { type = "string" },
            labels = new { type = "array", items = new { type = "string" } },
            confidence = new { type = "number" },
            questions = new { type = "array", items = new { type = "string" } },
            subTasks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    required = new[] { "title", "description" },
                    properties = new { title = new { type = "string" }, description = new { type = "string" } }
                }
            }
        }
    };

    private static MailAnalysis ParseAnalysis(object value)
    {
        var json = value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value);
        var analysis = JsonSerializer.Deserialize<MailAnalysis>(json, JsonDefaults.Options)
            ?? throw ApiException.BadRequest("Analiz sonucu beklenen biçimde değil.");
        analysis.Title = (analysis.Title ?? string.Empty).Trim();
        analysis.Description = (analysis.Description ?? string.Empty).Trim();
        analysis.Type = string.IsNullOrWhiteSpace(analysis.Type) ? "Talep" : analysis.Type.Trim();
        analysis.DueDate = analysis.DueDate ?? string.Empty;
        analysis.Labels = (analysis.Labels ?? new()).Select(item => (item ?? string.Empty).Trim()).Where(item => item.Length > 0).Take(5).ToList();
        analysis.Questions = (analysis.Questions ?? new()).Select(item => (item ?? string.Empty).Trim()).Where(item => item.Length > 0).Take(2).ToList();
        analysis.SubTasks = (analysis.SubTasks ?? new()).Where(item => item is not null).Take(8).ToList();
        if (analysis.Title.Length is < 3 or > 120) throw ApiException.BadRequest("Analiz sonucu beklenen biçimde değil: başlık uzunluğu geçersiz.");
        if (analysis.Description.Length is < 3 or > 4000) throw ApiException.BadRequest("Analiz sonucu beklenen biçimde değil: açıklama uzunluğu geçersiz.");
        if (analysis.Type is not ("Hata" or "Talep" or "Yeni özellik" or "Değişiklik")) throw ApiException.BadRequest("Analiz sonucu beklenen biçimde değil: ticket türü geçersiz.");
        if (analysis.DueDate.Length > 0 && !TicketDraftFormatter.IsDateOnly(analysis.DueDate)) analysis.DueDate = string.Empty;
        return analysis;
    }

    private async Task<MailIdentity> FindIdentityAsync(string email, string? subject, string? body, CancellationToken ct)
    {
        var person = await people.GetByEmailAsync(email, ct);
        if (person is null) return new MailIdentity(null, null, null, Array.Empty<Device>());

        var related = await devices.ListByPersonAsync(person.Id, ct);
        Device? selected = null;
        if (related.Count == 1)
        {
            selected = related[0];
        }
        else if (related.Count > 1)
        {
            var text = $"{subject}\n{body}".ToLowerInvariant();
            selected = related
                .Select(device => new { Device = device, Score = MatchScore(device, text) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Device)
                .FirstOrDefault();
        }
        var parentName = selected?.ParentId is { } parentId
            ? (await devices.GetByIdAsync(parentId, ct))?.Name
            : null;
        return new MailIdentity(person, selected, parentName, related);
    }

    private static int MatchScore(Device device, string text)
    {
        var terms = new[]
        {
            device.Name,
            device.Serial,
            device.IpAddress,
            device.Location
        };
        var score = 0;
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var normalized = term.Trim().ToLowerInvariant();
            if (normalized.Length >= 3 && text.Contains(normalized, StringComparison.Ordinal)) score += normalized.Length;
            foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 3 && text.Contains(token, StringComparison.Ordinal)) score += token.Length / 2;
            }
        }
        return score;
    }

    private static IEnumerable<string> ContextLines(MailIdentity identity)
    {
        if (identity.Person is { } person)
        {
            yield return $"Etkilenen çalışan: {person.Name}"
                + (!string.IsNullOrWhiteSpace(person.Department) ? $" / {person.Department}" : string.Empty)
                + (!string.IsNullOrWhiteSpace(person.Email) ? $" / {person.Email}" : string.Empty)
                + (!string.IsNullOrWhiteSpace(person.Phone) ? $" / {person.Phone}" : string.Empty);
        }
        if (identity.Device is { } device)
        {
            yield return $"Cihaz: {device.Name} ({device.Kind})";
            if (!string.IsNullOrWhiteSpace(device.IpAddress)) yield return $"İç IP: {device.IpAddress}";
            if (!string.IsNullOrWhiteSpace(device.Serial)) yield return $"Seri / envanter: {device.Serial}";
            if (!string.IsNullOrWhiteSpace(device.Location)) yield return $"Konum: {device.Location}";
            if (!string.IsNullOrWhiteSpace(device.Notes)) yield return $"Cihaz notu: {device.Notes}";
            if (!string.IsNullOrWhiteSpace(identity.ParentName)) yield return $"Bağlı cihaz: {identity.ParentName}";
        }
        else if (identity.Devices.Count > 1)
        {
            yield return $"Kayıtlı cihazlar: {string.Join(", ", identity.Devices.Select(device => device.Name))}";
        }
    }

    private sealed record MailIdentity(Person? Person, Device? Device, string? ParentName, IReadOnlyList<Device> Devices);

    private static List<T> SafeArray<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<T>>(json, JsonDefaults.Options);
            return parsed ?? new List<T>();
        }
        catch { return new List<T>(); }
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception is JsonException json
            ? $"Analiz sonucu beklenen biçimde değil: {json.Message}"
            : exception.Message;
        return Truncate(System.Text.RegularExpressions.Regex.Replace(message, "\\s+", " ").Trim(), MaxErrorLength);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static string StatusName(MailIngestionStatus status) => status == MailIngestionStatus.DryRun ? "dry_run" : status.ToString().ToLowerInvariant();
}


