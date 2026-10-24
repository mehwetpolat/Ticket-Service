using System.Text.Json;
using System.Text.Json.Nodes;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Infrastructure.Integrations;

public sealed partial class TicketPublisher(
    ISettingsService settings,
    ITicketRepository tickets,
    IUnitOfWork unitOfWork,
    IClock clock,
    HttpClient? httpClient = null) : ITicketPublisher
{
    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private readonly HttpClient _http = httpClient ?? DefaultHttp;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PublishResult> PublishAsync(string ownerId, string ticketId, TicketTarget target, string destinationId, TicketDraft draft, CancellationToken ct = default)
    {
        var definition = new TickeTask.Application.Tickets.TicketTargetCatalog().Get(target);
        var normalized = new TickeTask.Application.Tickets.TicketTargetCatalog().NormalizeDestination(target, destinationId);
        var error = new TickeTask.Application.Tickets.TicketTargetCatalog().DestinationError(target, normalized);
        if (error is not null) throw ApiException.BadRequest(error);
        if (target == TicketTarget.Jira) return await PublishJiraAsync(ownerId, ticketId, normalized, draft, ct);
        var providers = await settings.GetProvidersAsync(ownerId, ct);
        var config = ProviderConfig(providers, target);
        if (!new TickeTask.Application.Tickets.TicketTargetCatalog().ProviderConfigured(target, providers))
            throw ApiException.BadRequest($"{definition.Label} bağlantı bilgileri Ayarlar'da eksik.");
        return await PublishRemoteAsync(target, config, normalized, draft, ct);
    }

    private static Dictionary<string, string> ProviderConfig(ProviderSettings providers, TicketTarget target) => target switch
    {
        TicketTarget.Trello => new() { ["key"] = providers.Trello.Key, ["token"] = providers.Trello.Token },
        TicketTarget.Github => new() { ["token"] = providers.Github.Token, ["baseUrl"] = providers.Github.BaseUrl },
        TicketTarget.Asana => new() { ["token"] = providers.Asana.Token },
        TicketTarget.Clickup => new() { ["token"] = providers.Clickup.Token },
        _ => new()
    };

    private async Task<JsonNode> RequestJsonAsync(TicketTarget target, string url, HttpMethod method, string? body, Dictionary<string, string>? headers, CancellationToken ct)
    {
        var definition = new TickeTask.Application.Tickets.TicketTargetCatalog().Get(target);
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        if (headers is not null)
            foreach (var (key, value) in headers) request.Headers.TryAddWithoutValidation(key, value);
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw ApiException.Unsupported($"{definition.Label} sunucusuna ulaşılamadı: {exception.Message}");
        }
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? data = null;
        if (text.Length > 0)
        {
            try { data = JsonNode.Parse(text); } catch { }
        }
        if (!response.IsSuccessStatusCode)
        {
            var detail = ErrorDetail(data, text);
            var hint = (int)response.StatusCode switch
            {
                400 => " — gönderilen bilgiler reddedildi",
                401 => " — kimlik bilgileri reddedildi",
                403 => " — hesabın bu işlem için yetkisi yok",
                404 => " — hedef bulunamadı veya hesabın erişimi yok",
                410 => " — hedef arşivlenmiş veya ilgili özellik kapalı",
                422 => " — gönderilen alanlar reddedildi",
                429 => " — istek sınırı aşıldı, kısa süre sonra tekrar deneyin",
                _ => string.Empty
            };
            throw ApiException.BadRequest($"{definition.Label} isteği başarısız ({(int)response.StatusCode}{hint}){(detail.Length > 0 ? $": {detail}" : ".")}");
        }
        if (data is null) throw ApiException.BadRequest($"{definition.Label} beklenen JSON yanıtını döndürmedi.");
        return data;
    }

    private static string ErrorDetail(JsonNode? data, string text)
    {
        if (data is JsonObject value)
        {
            if (value["errors"] is JsonArray errors && errors.Count > 0)
            {
                var messages = errors.Select(item => item is JsonValue scalar ? scalar.GetValue<string>() : item?["message"]?.GetValue<string>() ?? item?["error"]?.GetValue<string>() ?? string.Empty)
                    .Where(item => item.Length > 0).ToList();
                if (messages.Count > 0) return Clean(string.Join("; ", messages));
            }
            foreach (var key in new[] { "message", "err", "error", "error_description" })
                if (value[key] is JsonValue scalar && scalar.TryGetValue<string>(out var message) && message.Trim().Length > 0)
                    return Clean(message);
        }
        return Clean(text);
    }

    private static string Clean(string value)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " "), "\\s+", " ").Trim();
        return cleaned[..Math.Min(400, cleaned.Length)];
    }
}

