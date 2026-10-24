using System.Text.Json.Nodes;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Infrastructure.Integrations;

public sealed class TicketProviderProbe(ISettingsService settings, HttpClient? httpClient = null) : ITicketProviderProbe
{
    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _http = httpClient ?? DefaultHttp;

    public async Task<ProbeResult> ProbeAsync(string ownerId, TicketTarget target, string destinationId, CancellationToken ct = default)
    {
        if (target == TicketTarget.Jira) throw ApiException.BadRequest("Jira bağlantısı ayrı Jira akışıyla test edilir.");
        var providers = await settings.GetProvidersAsync(ownerId, ct);
        var definition = new TickeTask.Application.Tickets.TicketTargetCatalog().Get(target);
        if (!new TickeTask.Application.Tickets.TicketTargetCatalog().ProviderConfigured(target, providers))
            throw ApiException.BadRequest($"{definition.Label} bağlantı bilgileri Ayarlar'da eksik.");
        var config = target switch
        {
            TicketTarget.Trello => new Dictionary<string, string> { ["key"] = providers.Trello.Key, ["token"] = providers.Trello.Token },
            TicketTarget.Github => new Dictionary<string, string> { ["token"] = providers.Github.Token, ["baseUrl"] = providers.Github.BaseUrl },
            TicketTarget.Asana => new Dictionary<string, string> { ["token"] = providers.Asana.Token },
            _ => new Dictionary<string, string> { ["token"] = providers.Clickup.Token }
        };
        var destination = string.IsNullOrWhiteSpace(destinationId) ? string.Empty :
            new TickeTask.Application.Tickets.TicketTargetCatalog().NormalizeDestination(target, destinationId);
        if (destination.Length > 0)
        {
            var error = new TickeTask.Application.Tickets.TicketTargetCatalog().DestinationError(target, destination);
            if (error is not null) throw ApiException.BadRequest(error);
        }

        var probe = new ProbeClient(_http, ct);
        switch (target)
        {
            case TicketTarget.Trello:
                {
                    var auth = $"key={Uri.EscapeDataString(config["key"])}&token={Uri.EscapeDataString(config["token"])}";
                    var me = await probe.GetAsync($"https://api.trello.com/1/members/me?{auth}&fields=id,username", null);
                    var name = string.Empty;
                    if (destination.Length > 0)
                    {
                        var list = await probe.GetAsync($"https://api.trello.com/1/lists/{Uri.EscapeDataString(destination)}?{auth}&fields=id,name,closed", null);
                        if (list["closed"]?.GetValue<bool>() == true) throw ApiException.BadRequest($"Trello listesi \"{list["name"]?.GetValue<string>() ?? destination}\" arşivlenmiş. Açık bir liste seçin.");
                        name = list["name"]?.GetValue<string>() ?? string.Empty;
                    }
                    return new ProbeResult(me["username"]?.GetValue<string>() ?? me["id"]?.GetValue<string>() ?? string.Empty, destination.Length > 0, name);
                }
            case TicketTarget.Github:
                {
                    var apiBase = SettingsService.NormalizeApiBaseUrl(config.GetValueOrDefault("baseUrl"), "https://api.github.com");
                    var me = await probe.GetAsync($"{apiBase}/user", TicketPublisher.GitHubHeaders(config["token"]));
                    var name = string.Empty;
                    if (destination.Length > 0)
                    {
                        var repo = await probe.GetAsync($"{apiBase}/repos/{destination}", TicketPublisher.GitHubHeaders(config["token"]));
                        if (repo["has_issues"]?.GetValue<bool>() == false) throw ApiException.BadRequest($"GitHub deposu \"{repo["full_name"]?.GetValue<string>() ?? destination}\" için Issues sekmesi kapalı.");
                        name = repo["full_name"]?.GetValue<string>() ?? string.Empty;
                    }
                    return new ProbeResult(me["login"]?.GetValue<string>() ?? string.Empty, destination.Length > 0, name);
                }
            case TicketTarget.Asana:
                {
                    var headers = new Dictionary<string, string> { ["Accept"] = "application/json", ["Authorization"] = $"Bearer {config["token"]}" };
                    var me = await probe.GetAsync("https://app.asana.com/api/1.0/users/me", headers);
                    var name = string.Empty;
                    if (destination.Length > 0)
                    {
                        var project = await probe.GetAsync($"https://app.asana.com/api/1.0/projects/{Uri.EscapeDataString(destination)}?opt_fields=name,archived", headers);
                        if (project["data"]?["archived"]?.GetValue<bool>() == true) throw ApiException.BadRequest($"Asana projesi \"{project["data"]?["name"]?.GetValue<string>() ?? destination}\" arşivlenmiş. Açık bir proje seçin.");
                        name = project["data"]?["name"]?.GetValue<string>() ?? string.Empty;
                    }
                    return new ProbeResult(me["data"]?["name"]?.GetValue<string>() ?? me["data"]?["gid"]?.GetValue<string>() ?? string.Empty, destination.Length > 0, name);
                }
            case TicketTarget.Clickup:
                {
                    var headers = new Dictionary<string, string> { ["Accept"] = "application/json", ["Authorization"] = config["token"] };
                    var me = await probe.GetAsync("https://api.clickup.com/api/v2/user", headers);
                    var name = string.Empty;
                    if (destination.Length > 0)
                    {
                        var list = await probe.GetAsync($"https://api.clickup.com/api/v2/list/{Uri.EscapeDataString(destination)}", headers);
                        if (list["archived"]?.GetValue<bool>() == true) throw ApiException.BadRequest($"ClickUp listesi \"{list["name"]?.GetValue<string>() ?? destination}\" arşivlenmiş. Açık bir liste seçin.");
                        name = list["name"]?.GetValue<string>() ?? string.Empty;
                    }
                    return new ProbeResult(me["user"]?["username"]?.GetValue<string>() ?? me["user"]?["id"]?.GetValue<string>() ?? string.Empty, destination.Length > 0, name);
                }
            default:
                throw ApiException.BadRequest("Desteklenmeyen ticket hedefi.");
        }
    }

    private sealed class ProbeClient(HttpClient http, CancellationToken ct)
    {
        public async Task<JsonObject> GetAsync(string url, Dictionary<string, string>? headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers is not null)
                foreach (var (key, value) in headers) request.Headers.TryAddWithoutValidation(key, value);
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? data = null;
            try { data = JsonNode.Parse(text); } catch { }
            if (!response.IsSuccessStatusCode)
            {
                var detail = data?["message"]?.GetValue<string>() ?? text;
                throw ApiException.BadRequest($"Sağlayıcı isteği başarısız ({(int)response.StatusCode}): {detail}");
            }
            return data?.AsObject() ?? new JsonObject();
        }
    }
}

