using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Application.Tickets;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Infrastructure.Integrations;

public sealed partial class TicketPublisher
{
    private sealed record JiraIssueType(string Id, string Name, bool Subtask);
    private sealed record JiraField(string FieldId, bool Required, List<JiraAllowedValue>? AllowedValues);
    private sealed record JiraAllowedValue(string Id, string Name);

    private async Task<PublishResult> PublishJiraAsync(string ownerId, string ticketId, string destinationId, TicketDraft draft, CancellationToken ct)
    {
        var providers = await settings.GetProvidersAsync(ownerId, ct);
        var config = providers.Jira;
        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Email) || string.IsNullOrWhiteSpace(config.Token))
            throw ApiException.BadRequest("Jira bağlantı bilgileri Ayarlar'da eksik.");
        var baseUrl = SettingsService.NormalizeJiraBaseUrl(config.BaseUrl);
        var authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Email}:{config.Token}"));
        var ticket = await tickets.GetAsync(ticketId, ownerId, ct) ?? throw ApiException.NotFound("Ticket bulunamadı.");
        var subTasks = Deserialize<List<TicketSubtask>>(ticket.SubTasksJson) ?? new();
        var attachments = Deserialize<List<TicketAttachment>>(ticket.AttachmentsJson) ?? new();

        var issueTypes = await JiraGetAsync(baseUrl, $"/rest/api/3/issue/createmeta/{Uri.EscapeDataString(destinationId)}/issuetypes?maxResults=100", authorization, ct);
        var types = ParseIssueTypes(issueTypes);
        var parentType = SelectParentType(types, draft.Type);
        var parentFields = await CreateFieldsAsync(baseUrl, destinationId, parentType.Id, authorization, ct);
        ValidateFields(parentFields, new[] { "project", "issuetype", "summary", "reporter" }, "ana ticket");
        var childType = subTasks.Count > 0 ? types.FirstOrDefault(type => type.Subtask) : null;
        if (subTasks.Count > 0 && childType is null)
        {
            subTasks.Clear();
            ticket.SubTasksJson = "[]";
            ticket.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);
        }
        var childFields = childType is not null ? await CreateFieldsAsync(baseUrl, destinationId, childType.Id, authorization, ct) : new List<JiraField>();
        if (childType is not null) ValidateFields(childFields, new[] { "project", "issuetype", "summary", "reporter", "parent" }, "alt görev");

        if (attachments.Count > 0)
        {
            var meta = await JiraGetAsync(baseUrl, "/rest/api/3/attachment/meta", authorization, ct);
            if (meta["enabled"]?.GetValue<bool>() != true) throw ApiException.BadRequest("Jira projesinde ek yükleme kapalı.");
            var limit = meta["uploadLimit"]?.GetValue<long>() ?? 0;
            var oversized = attachments.FirstOrDefault(item => !item.Uploaded && limit > 0 && item.Data.Length * 3L / 4L > limit);
            if (oversized is not null) throw ApiException.BadRequest($"{oversized.Name} Jira'nın ek boyutu sınırını aşıyor.");
        }

        var needsReporter = parentFields.Concat(childFields).Any(field => field.FieldId == "reporter" && field.Required);
        string? reporterId = null;
        if (needsReporter)
        {
            var myself = await JiraGetAsync(baseUrl, "/rest/api/3/myself", authorization, ct);
            reporterId = myself["accountId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(reporterId)) throw ApiException.BadRequest("Jira kullanıcı kimliği alınamadı.");
        }

        var parentKey = ticket.ExternalId;
        if (string.IsNullOrEmpty(parentKey))
        {
            var fields = IssueFields(destinationId, parentType.Id, draft.Title, draft.Description, parentFields, reporterId, draft);
            var result = await JiraPostIssueAsync(baseUrl, authorization, fields, ct);
            parentKey = result["key"]?.GetValue<string>() ?? throw ApiException.BadRequest("Jira ticket anahtarını döndürmedi.");
            ticket.ExternalId = parentKey;
            ticket.ExternalUrl = $"{baseUrl}/browse/{parentKey}";
            ticket.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);
        }

        for (var index = 0; index < subTasks.Count; index++)
        {
            var task = subTasks[index];
            if (!string.IsNullOrEmpty(task.ExternalId)) continue;
            var fields = IssueFields(destinationId, childType!.Id, task.Title, string.IsNullOrWhiteSpace(task.Description) ? task.Title : task.Description, childFields, reporterId);
            fields["parent"] = new JsonObject { ["key"] = parentKey };
            var result = await JiraPostIssueAsync(baseUrl, authorization, fields, ct);
            task.ExternalId = result["key"]?.GetValue<string>();
            ticket.SubTasksJson = JsonSerializer.Serialize(subTasks, JsonOptions);
            ticket.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);
        }

        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            if (attachment.Uploaded) continue;
            await UploadAttachmentAsync(baseUrl, authorization, parentKey!, attachment, ct);
            attachment.Data = string.Empty;
            attachment.Uploaded = true;
            ticket.AttachmentsJson = JsonSerializer.Serialize(attachments, JsonOptions);
            ticket.UpdatedAt = clock.UnixNow;
            await unitOfWork.SaveChangesAsync(ct);
        }

        return new PublishResult(parentKey!, $"{baseUrl}/browse/{parentKey}");
    }

    private static List<JiraIssueType> ParseIssueTypes(JsonObject data) =>
        data["issueTypes"]?.AsArray().Select(item => new JiraIssueType(
            item?["id"]?.GetValue<string>() ?? string.Empty,
            item?["name"]?.GetValue<string>() ?? string.Empty,
            item?["subtask"]?.GetValue<bool>() ?? false)).Where(item => item.Id.Length > 0).ToList() ?? new();

    private static JiraIssueType SelectParentType(List<JiraIssueType> types, string draftType)
    {
        var preferences = new Dictionary<string, string[]>
        {
            ["Hata"] = new[] { "Bug", "Hata", "Incident", "Task", "Görev" },
            ["Talep"] = new[] { "Service Request", "Talep", "Request", "Task", "Görev" },
            ["Yeni özellik"] = new[] { "Story", "Hikaye", "Feature", "Yeni özellik", "Task", "Görev" },
            ["Değişiklik"] = new[] { "Change", "Değişiklik", "Task", "Görev" }
        };
        var available = types.Where(type => !type.Subtask).ToList();
        var wanted = preferences.TryGetValue(draftType, out var names) ? names : new[] { "Task", "Görev" };
        foreach (var name in wanted)
        {
            var match = available.FirstOrDefault(type => type.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null) return match;
        }
        throw ApiException.BadRequest($"Jira projesinde uygun ticket türü bulunamadı. Mevcut türler: {(available.Count > 0 ? string.Join(", ", available.Select(type => type.Name)) : "yok")}.");
    }

    private async Task<List<JiraField>> CreateFieldsAsync(string baseUrl, string project, string typeId, string authorization, CancellationToken ct)
    {
        var data = await JiraGetAsync(baseUrl, $"/rest/api/3/issue/createmeta/{Uri.EscapeDataString(project)}/issuetypes/{Uri.EscapeDataString(typeId)}?maxResults=100", authorization, ct);
        return data["fields"]?.AsArray().Select(item => new JiraField(
            item?["fieldId"]?.GetValue<string>() ?? string.Empty,
            item?["required"]?.GetValue<bool>() ?? false,
            item?["allowedValues"]?.AsArray().Select(value => new JiraAllowedValue(
                value?["id"]?.GetValue<string>() ?? string.Empty,
                value?["name"]?.GetValue<string>() ?? string.Empty)).ToList())).Where(field => field.FieldId.Length > 0).ToList() ?? new();
    }

    private static void ValidateFields(List<JiraField> fields, string[] supported, string label)
    {
        var extra = fields.Where(field => field.Required && !supported.Contains(field.FieldId)).Select(field => field.FieldId).ToList();
        if (extra.Count > 0) throw ApiException.BadRequest($"Jira {label} oluştururken ek zorunlu alan istiyor: {string.Join(", ", extra)}.");
        if (!fields.Any(field => field.FieldId == "description")) throw ApiException.BadRequest($"Jira {label} oluşturma ekranında açıklama alanı yok.");
    }

    private static JsonObject IssueFields(string project, string typeId, string title, string description, List<JiraField> fields, string? reporterId, TicketDraft? draft = null)
    {
        var available = fields.Select(field => field.FieldId).ToHashSet();
        var priority = fields.FirstOrDefault(field => field.FieldId == "priority");
        var priorityNames = draft?.Priority == TicketPriority.Critical
            ? new[] { "Highest", "Critical", "Blocker", "Kritik" }
            : draft?.Priority == TicketPriority.High ? new[] { "High", "Yüksek" } : new[] { "Medium", "Normal", "Orta" };
        JiraAllowedValue? priorityValue = null;
        if (priority?.AllowedValues is not null)
            foreach (var name in priorityNames)
            {
                priorityValue = priority.AllowedValues.FirstOrDefault(value => value.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                if (priorityValue is not null) break;
            }
        var result = new JsonObject
        {
            ["project"] = new JsonObject { ["key"] = project },
            ["issuetype"] = new JsonObject { ["id"] = typeId },
            ["summary"] = title,
            ["description"] = AdfDescription(description)
        };
        if (!string.IsNullOrEmpty(reporterId)) result["reporter"] = new JsonObject { ["id"] = reporterId };
        if (draft is not null && draft.Labels.Count > 0 && available.Contains("labels"))
            result["labels"] = new JsonArray(draft.Labels.Select(label => JsonValue.Create(TicketDraftFormatter.Slug(label))).Where(value => value is not null).ToArray()!);
        if (draft is not null && TicketDraftFormatter.IsDateOnly(draft.DueDate) && available.Contains("duedate")) result["duedate"] = draft.DueDate;
        if (priorityValue is not null) result["priority"] = new JsonObject { ["id"] = priorityValue.Id };
        return result;
    }

    private static JsonObject AdfDescription(string value)
    {
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length > 0).Take(120)
            .Select(line => (JsonNode)new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = line[..Math.Min(4000, line.Length)] })
            }).ToArray();
        if (lines.Length == 0)
            lines = new JsonNode[] { new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "(Açıklama yok)" }) } };
        return new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = new JsonArray(lines) };
    }

    private async Task<JsonObject> JiraGetAsync(string baseUrl, string path, string authorization, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw ApiException.BadRequest($"Jira proje bilgileri alınamadı ({(int)response.StatusCode}). Proje anahtarını ve erişim yetkisini kontrol edin.");
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
    }

    private async Task<JsonObject> JiraPostIssueAsync(string baseUrl, string authorization, JsonObject fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/api/3/issue");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Content = new StringContent(new JsonObject { ["fields"] = fields }.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = "Jira isteği reddetti.";
            try
            {
                var parsed = JsonNode.Parse(text)?.AsObject();
                var messages = parsed?["errorMessages"]?.AsArray().Select(item => item?.GetValue<string>() ?? string.Empty).Where(item => item.Length > 0).ToList() ?? new();
                var fieldErrors = parsed?["errors"]?.AsObject().Select(pair => $"{pair.Key}: {pair.Value?.GetValue<string>()}").ToList() ?? new();
                var combined = string.Join("; ", messages.Concat(fieldErrors));
                if (combined.Length > 0) detail = combined[..Math.Min(500, combined.Length)].Replace("\r", " ").Replace("\n", " ");
            }
            catch { }
            throw ApiException.BadRequest($"Jira ticket oluşturmadı ({(int)response.StatusCode}): {detail}");
        }
        return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
    }

    private async Task UploadAttachmentAsync(string baseUrl, string authorization, string issueKey, TicketAttachment attachment, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/attachments");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("X-Atlassian-Token", "no-check");
        var content = new MultipartFormDataContent();
        var bytes = Convert.FromBase64String(attachment.Data);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(attachment.ContentType);
        content.Add(file, "file", attachment.Name);
        request.Content = content;
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            throw ApiException.BadRequest($"Jira ek yükleyemedi ({(int)response.StatusCode}): {Clean(text)}");
        }
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return null; }
    }
}

