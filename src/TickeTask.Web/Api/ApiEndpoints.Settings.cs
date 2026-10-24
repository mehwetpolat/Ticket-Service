using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Services;
using TickeTask.Web.Security;

namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    private static readonly HashSet<string> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        "jira", "trello", "github", "asana", "clickup"
    };

    private static readonly HttpClient ProbeHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static void MapSettings(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", async (ISessionAuthService auth, ISettingsService settings) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var providers = await settings.GetProvidersAsync(ownerId);
            var llm = await settings.GetLlmAsync(ownerId);
            return ApiSupport.Json(new
            {
                llm,
                providers = new Dictionary<string, object>
                {
                    ["jira"] = new
                    {
                        baseUrl = providers.Jira.BaseUrl,
                        email = providers.Jira.Email,
                        hasToken = !string.IsNullOrWhiteSpace(providers.Jira.Token),
                        hasBaseUrl = !string.IsNullOrWhiteSpace(providers.Jira.BaseUrl)
                    },
                    ["trello"] = new
                    {
                        hasToken = !string.IsNullOrWhiteSpace(providers.Trello.Token),
                        hasKey = !string.IsNullOrWhiteSpace(providers.Trello.Key)
                    },
                    ["github"] = new
                    {
                        baseUrl = providers.Github.BaseUrl,
                        hasToken = !string.IsNullOrWhiteSpace(providers.Github.Token),
                        hasBaseUrl = !string.IsNullOrWhiteSpace(providers.Github.BaseUrl)
                    },
                    ["asana"] = new { hasToken = !string.IsNullOrWhiteSpace(providers.Asana.Token) },
                    ["clickup"] = new { hasToken = !string.IsNullOrWhiteSpace(providers.Clickup.Token) }
                }
            });
        });

        group.MapPost("/", async (HttpContext context, ISessionAuthService auth, ISettingsService settings,
            IIdGenerator ids) =>
        {
            await auth.RequireAdminAsync();
            ApiSupport.Limit(context.Request, 100_000, "İstek çok büyük.");
            var ownerId = await auth.GetOwnerIdAsync();
            var root = await ApiSupport.RawBody(context.Request);
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (type == "llm")
            {
                var endpoint = root.TryGetProperty("endpoint", out var endpointValue) ? endpointValue.GetString() ?? string.Empty : string.Empty;
                var model = root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() ?? string.Empty : string.Empty;
                if (!System.Text.RegularExpressions.Regex.IsMatch(model, "^[a-z0-9][a-z0-9._:/-]{1,100}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw ApiException.BadRequest("Model adı geçersiz.");
                await settings.SaveLlmAsync(ownerId, new LlmSettings { Endpoint = endpoint, Model = model });
                return ApiSupport.Json(new { ok = true });
            }
            if (type == "provider")
            {
                var provider = root.TryGetProperty("provider", out var providerValue) ? providerValue.GetString() ?? string.Empty : string.Empty;
                if (!Providers.Contains(provider)) throw ApiException.BadRequest("Geçersiz entegrasyon.");
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("values", out var valuesElement) && valuesElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in valuesElement.EnumerateObject())
                    {
                        var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : string.Empty;
                        if (value.Length > 500) throw ApiException.BadRequest("Ayar değeri çok uzun.");
                        values[property.Name] = value;
                    }
                }
                await settings.SaveProviderAsync(ownerId, provider.ToLowerInvariant(), values);
                return ApiSupport.Json(new { ok = true });
            }
            return ApiSupport.Error(400, "Geçersiz ayar.");
        });

        group.MapDelete("/", async (ISessionAuthService auth, ISettingsService settings, HttpRequest request) =>
        {
            await auth.RequireAdminAsync();
            var ownerId = await auth.GetOwnerIdAsync();
            var provider = request.Query["provider"].ToString();
            if (string.IsNullOrWhiteSpace(provider) || !Providers.Contains(provider))
                throw ApiException.BadRequest("Geçersiz entegrasyon.");
            await settings.RemoveProviderAsync(ownerId, provider.ToLowerInvariant());
            return ApiSupport.Json(new { ok = true });
        });

        app.MapPost("/api/settings/test", async (HttpContext context, ISessionAuthService authService,
            SessionAuthService auth, ISettingsService settings, IProjectService project, ITicketProviderProbe probe,
            IRateLimiter limiter, IClock clock, ITicketTargetCatalog catalog, HttpRequest request) =>
        {
            await authService.RequireAdminAsync();
            ApiSupport.Limit(request, 5_000, "İstek çok büyük.");
            ApiSupport.RateLimited(limiter, $"settings-test:{auth.ClientAddress()}", 10, 5 * 60);
            var ownerId = await authService.GetOwnerIdAsync();
            var input = await ApiSupport.Body<TestProviderRequest>(request);
            var provider = (input.Provider ?? string.Empty).ToLowerInvariant();
            if (!Providers.Contains(provider)) throw ApiException.BadRequest("Geçersiz entegrasyon.");
            var config = await settings.GetProvidersAsync(ownerId);
            var projectConfig = await project.GetAsync(ownerId);
            var target = ParseTarget(provider);
            var destinationId = projectConfig.Target == target ? projectConfig.DestinationId : string.Empty;
            var destinationChecked = false;
            var destinationName = string.Empty;

            if (target == TicketTarget.Jira)
            {
                if (string.IsNullOrWhiteSpace(config.Jira.BaseUrl) || string.IsNullOrWhiteSpace(config.Jira.Email) || string.IsNullOrWhiteSpace(config.Jira.Token))
                    throw ApiException.BadRequest("Jira bilgileri eksik.");
                var baseUrl = SettingsService.NormalizeJiraBaseUrl(config.Jira.BaseUrl);
                var authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Jira.Email}:{config.Jira.Token}")));
                using (var myself = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/rest/api/3/myself"))
                {
                    myself.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    myself.Headers.Authorization = authorization;
                    using var response = await ProbeHttp.SendAsync(myself);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw ApiException.BadRequest("Jira kimlik bilgileri reddedildi (401). Atlassian yönetici API key'i yerine kullanıcı API tokenı ve o hesabın e-postasını girin.");
                    if (!response.IsSuccessStatusCode)
                        throw ApiException.BadRequest($"Jira bağlantısı doğrulanamadı ({(int)response.StatusCode}).");
                }
                if (!string.IsNullOrWhiteSpace(destinationId))
                {
                    using var projectRequest = new HttpRequestMessage(HttpMethod.Get,
                        $"{baseUrl}/rest/api/3/issue/createmeta/{Uri.EscapeDataString(destinationId)}/issuetypes?maxResults=100");
                    projectRequest.Headers.Authorization = authorization;
                    projectRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    using var projectResponse = await ProbeHttp.SendAsync(projectRequest);
                    if (!projectResponse.IsSuccessStatusCode)
                        throw ApiException.BadRequest($"Jira kimliği doğrulandı ancak \"{destinationId}\" projesine erişilemedi ({(int)projectResponse.StatusCode}). Proje anahtarını ve hesabın proje iznini kontrol edin.");
                    destinationChecked = true;
                    destinationName = destinationId;
                }
            }
            else
            {
                var result = await probe.ProbeAsync(ownerId, target, destinationId);
                destinationChecked = result.DestinationChecked;
                destinationName = result.DestinationName;
            }

            await settings.SetAsync(ownerId, "ticket_provider_last_test", new
            {
                at = clock.UnixNow,
                target = provider,
                destinationId = destinationChecked ? destinationId : string.Empty,
                destinationChecked,
                destinationName
            });
            if (target == TicketTarget.Jira)
                await settings.SetAsync(ownerId, "jira_last_test_at", new { at = clock.UnixNow, projectChecked = destinationChecked });
            return ApiSupport.Json(new { ok = true, projectChecked = destinationChecked, destinationChecked, destinationName });
        });
    }

    private static TicketTarget ParseTarget(string provider) => provider switch
    {
        "jira" => TicketTarget.Jira,
        "trello" => TicketTarget.Trello,
        "github" => TicketTarget.Github,
        "asana" => TicketTarget.Asana,
        "clickup" => TicketTarget.Clickup,
        _ => throw ApiException.BadRequest("Geçersiz entegrasyon.")
    };
}
