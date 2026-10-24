using TickeTask.Application.Abstractions;
using System.Text.Json.Nodes;
using TickeTask.Application.Common;
using TickeTask.Application.Tickets;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Infrastructure.Integrations;

public sealed partial class TicketPublisher
{
    private async Task<PublishResult> PublishRemoteAsync(TicketTarget target, Dictionary<string, string> config, string destination, TicketDraft draft, CancellationToken ct)
    {
        var description = TicketDraftFormatter.Format(draft);
        switch (target)
        {
            case TicketTarget.Trello:
                {
                    var query = new Dictionary<string, string>
                    {
                        ["key"] = config["key"],
                        ["token"] = config["token"],
                        ["idList"] = destination,
                        ["name"] = draft.Title,
                        ["desc"] = description,
                        ["pos"] = "bottom"
                    };
                    if (TicketDraftFormatter.IsDateOnly(draft.DueDate)) query["due"] = $"{draft.DueDate}T17:00:00.000Z";
                    var url = "https://api.trello.com/1/cards?" + string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
                    var card = await RequestJsonAsync(target, url, HttpMethod.Post, null, null, ct);
                    var id = card["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id)) throw ApiException.BadRequest("Trello kart kimliğini döndürmedi.");
                    var cardUrl = card["url"]?.GetValue<string>() ?? card["shortUrl"]?.GetValue<string>() ?? $"https://trello.com/c/{card["shortLink"]?.GetValue<string>() ?? id}";
                    return new PublishResult(id, cardUrl);
                }
            case TicketTarget.Github:
                {
                    var apiBase = SettingsService.NormalizeApiBaseUrl(config.GetValueOrDefault("baseUrl"), "https://api.github.com");
                    var issue = await RequestJsonAsync(target, $"{apiBase}/repos/{destination}/issues", HttpMethod.Post,
                        new JsonObject { ["title"] = draft.Title, ["body"] = description }.ToJsonString(), GitHubHeaders(config["token"]), ct);
                    var number = issue["number"]?.GetValue<long>();
                    var htmlUrl = issue["html_url"]?.GetValue<string>();
                    if (number is null || string.IsNullOrEmpty(htmlUrl)) throw ApiException.BadRequest("GitHub issue numarasını döndürmedi.");
                    return new PublishResult(number.Value.ToString(), htmlUrl);
                }
            case TicketTarget.Asana:
                {
                    var body = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["name"] = draft.Title,
                            ["notes"] = description,
                            ["projects"] = new JsonArray(destination)
                        }
                    };
                    if (TicketDraftFormatter.IsDateOnly(draft.DueDate)) body["data"]!["due_on"] = draft.DueDate;
                    var task = await RequestJsonAsync(target, "https://app.asana.com/api/1.0/tasks?opt_fields=permalink_url", HttpMethod.Post,
                        body.ToJsonString(), new Dictionary<string, string>
                        {
                            ["Accept"] = "application/json",
                            ["Authorization"] = $"Bearer {config["token"]}"
                        }, ct);
                    var gid = task["data"]?["gid"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(gid)) throw ApiException.BadRequest("Asana görev kimliğini döndürmedi.");
                    return new PublishResult(gid, task["data"]?["permalink_url"]?.GetValue<string>() ?? $"https://app.asana.com/0/0/{gid}");
                }
            case TicketTarget.Clickup:
                {
                    var priority = draft.Priority == TicketPriority.Critical ? 1 : draft.Priority == TicketPriority.High ? 2 : 3;
                    var body = new JsonObject { ["name"] = draft.Title, ["description"] = description, ["priority"] = priority };
                    if (TicketDraftFormatter.IsDateOnly(draft.DueDate)) body["due_date"] = TicketDraftFormatter.UnixAt(draft.DueDate, 17);
                    var task = await RequestJsonAsync(target, $"https://api.clickup.com/api/v2/list/{Uri.EscapeDataString(destination)}/task", HttpMethod.Post,
                        body.ToJsonString(), new Dictionary<string, string>
                        {
                            ["Accept"] = "application/json",
                            ["Authorization"] = config["token"]
                        }, ct);
                    var id = task["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id)) throw ApiException.BadRequest("ClickUp görev kimliğini döndürmedi.");
                    return new PublishResult(id, task["url"]?.GetValue<string>() ?? $"https://app.clickup.com/t/{id}");
                }
            default:
                throw ApiException.BadRequest("Desteklenmeyen ticket hedefi.");
        }
    }

    internal static Dictionary<string, string> GitHubHeaders(string token) => new()
    {
        ["Accept"] = "application/vnd.github+json",
        ["Authorization"] = $"Bearer {token}",
        ["X-GitHub-Api-Version"] = "2022-11-28",
        ["User-Agent"] = "TickeTask"
    };
}



