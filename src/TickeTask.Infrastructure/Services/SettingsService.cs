using System.Net;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Services;

public sealed class SettingsService(ISettingRepository settings) : ISettingsService
{
    public Task<T?> GetAsync<T>(string ownerId, string key, CancellationToken ct = default) =>
        settings.GetAsync<T>(ownerId, key, ct);

    public Task SetAsync<T>(string ownerId, string key, T value, CancellationToken ct = default) =>
        settings.SetAsync(ownerId, key, value, ct);

    public async Task<ProviderSettings> GetProvidersAsync(string ownerId, CancellationToken ct = default)
    {
        var saved = await settings.GetAsync<ProviderSettings>(ownerId, "providers", ct);
        var providers = new ProviderSettings
        {
            Jira = new JiraProviderSettings
            {
                BaseUrl = Env("JIRA_BASE_URL") ?? string.Empty,
                Email = Env("JIRA_EMAIL") ?? string.Empty,
                Token = Env("JIRA_API_TOKEN") ?? string.Empty
            },
            Trello = new TrelloProviderSettings
            {
                Key = Env("TRELLO_API_KEY") ?? string.Empty,
                Token = Env("TRELLO_TOKEN") ?? string.Empty
            },
            Github = new GithubProviderSettings
            {
                Token = Env("GITHUB_TOKEN") ?? string.Empty,
                BaseUrl = Env("GITHUB_API_URL") ?? string.Empty
            },
            Asana = new AsanaProviderSettings { Token = Env("ASANA_TOKEN") ?? string.Empty },
            Clickup = new ClickupProviderSettings { Token = Env("CLICKUP_TOKEN") ?? string.Empty }
        };
        if (saved is null) return providers;
        providers.Jira = saved.Jira;
        providers.Trello = saved.Trello;
        providers.Github = saved.Github;
        providers.Asana = saved.Asana;
        providers.Clickup = saved.Clickup;
        return providers;
    }

    public async Task<LlmSettings> GetLlmAsync(string ownerId, CancellationToken ct = default) =>
        await settings.GetAsync<LlmSettings>(ownerId, "llm", ct) ?? new LlmSettings
        {
            Endpoint = Env("OLLAMA_URL") ?? "http://127.0.0.1:11434",
            Model = "qwen3:4b"
        };

    public async Task SaveProviderAsync(string ownerId, string provider, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        var providers = await GetProvidersAsync(ownerId, ct);
        switch (provider)
        {
            case "jira":
                providers.Jira = new JiraProviderSettings
                {
                    BaseUrl = Clean(values, "baseUrl", providers.Jira.BaseUrl),
                    Email = Clean(values, "email", providers.Jira.Email),
                    Token = Clean(values, "token", providers.Jira.Token)
                };
                if (providers.Jira.BaseUrl.Length > 0) providers.Jira.BaseUrl = NormalizeJiraBaseUrl(providers.Jira.BaseUrl);
                break;
            case "trello":
                providers.Trello = new TrelloProviderSettings
                {
                    Key = Clean(values, "key", providers.Trello.Key),
                    Token = Clean(values, "token", providers.Trello.Token)
                };
                break;
            case "github":
                providers.Github = new GithubProviderSettings
                {
                    Token = Clean(values, "token", providers.Github.Token),
                    BaseUrl = Clean(values, "baseUrl", providers.Github.BaseUrl)
                };
                if (providers.Github.BaseUrl.Length > 0) providers.Github.BaseUrl = NormalizeApiBaseUrl(providers.Github.BaseUrl, string.Empty);
                break;
            case "asana":
                providers.Asana = new AsanaProviderSettings { Token = Clean(values, "token", providers.Asana.Token) };
                break;
            case "clickup":
                providers.Clickup = new ClickupProviderSettings { Token = Clean(values, "token", providers.Clickup.Token) };
                break;
            default:
                throw ApiException.BadRequest("Geçersiz entegrasyon.");
        }
        await settings.SetAsync(ownerId, "providers", providers, ct);
    }

    public async Task RemoveProviderAsync(string ownerId, string provider, CancellationToken ct = default)
    {
        var providers = await GetProvidersAsync(ownerId, ct);
        switch (provider)
        {
            case "jira": providers.Jira = new(); break;
            case "trello": providers.Trello = new(); break;
            case "github": providers.Github = new(); break;
            case "asana": providers.Asana = new(); break;
            case "clickup": providers.Clickup = new(); break;
            default: throw ApiException.BadRequest("Geçersiz entegrasyon.");
        }
        await settings.SetAsync(ownerId, "providers", providers, ct);
    }

    public async Task SaveLlmAsync(string ownerId, LlmSettings value, CancellationToken ct = default)
    {
        value.Endpoint = NormalizeLlmEndpoint(value.Endpoint);
        await settings.SetAsync(ownerId, "llm", value, ct);
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Clean(IReadOnlyDictionary<string, string> values, string field, string existing) =>
        values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() :
        field == "baseUrl" ? string.Empty : existing;

    public static string NormalizeJiraBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            throw ApiException.BadRequest("Jira adresi geçerli bir URL olmalı.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw ApiException.BadRequest("Jira adresi HTTPS olmalı. Örnek: https://sirket.atlassian.net");
        if (uri.UserInfo.Length > 0)
            throw ApiException.BadRequest("Jira adresine kullanıcı adı veya parola eklenmemeli.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string NormalizeApiBaseUrl(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            throw ApiException.BadRequest("API adresi geçerli bir HTTPS URL olmalıdır.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw ApiException.BadRequest("API adresi HTTPS olmalıdır.");
        if (uri.UserInfo.Length > 0)
            throw ApiException.BadRequest("API adresine kullanıcı adı veya parola eklenmemelidir.");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw ApiException.BadRequest("API adresi sorgu veya çapa içermemelidir.");
        return uri.ToString().TrimEnd('/');
    }

    public static string NormalizeLlmEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw ApiException.BadRequest("Model adresi geçerli bir URL olmalı.");
        var host = uri.Host.ToLowerInvariant();
        var privateHost = host is "ollama" or "localhost" or "127.0.0.1" or "::1" or "host.docker.internal" or "gateway.docker.internal"
            || host.StartsWith("10.") || host.StartsWith("192.168.") || System.Text.RegularExpressions.Regex.IsMatch(host, "^172\\.(1[6-9]|2\\d|3[01])\\.");
        if (uri.Scheme != Uri.UriSchemeHttp || !privateHost || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw ApiException.BadRequest("Model adresi yalnızca şirket içi HTTP sunucusu olabilir.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

public sealed class ProjectService(ISettingsService settings, ICustomerRepository customers) : IProjectService
{
    public async Task<ProjectConfig> GetAsync(string ownerId, CancellationToken ct = default)
    {
        var saved = await settings.GetAsync<ProjectConfig>(ownerId, "project", ct);
        var target = saved?.Target ?? TicketTarget.Jira;
        var name = string.IsNullOrWhiteSpace(saved?.Name) ? "Kurum içi BT" : saved!.Name;
        if (!string.IsNullOrWhiteSpace(saved?.DestinationId))
            return new ProjectConfig { Target = target, DestinationId = Normalize(target, saved.DestinationId), Name = name };
        var all = await customers.ListAsync(ownerId, ct);
        var legacy = all.FirstOrDefault(customer => customer.Target == TicketTarget.Jira && customer.DestinationId.Length > 0);
        return new ProjectConfig { Target = TicketTarget.Jira, DestinationId = legacy?.DestinationId ?? string.Empty, Name = name };
    }

    public async Task SaveAsync(string ownerId, ProjectConfig config, CancellationToken ct = default)
    {
        config.Name = (config.Name ?? string.Empty).Trim();
        config.DestinationId = (config.DestinationId ?? string.Empty).Trim();
        await settings.SetAsync(ownerId, "project", config, ct);
    }

    private static string Normalize(TicketTarget target, string value)
    {
        var catalog = new TickeTask.Application.Tickets.TicketTargetCatalog();
        return catalog.NormalizeDestination(target, value);
    }
}

