using System.Net;
using System.Text;
using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Integrations;
using TickeTask.Infrastructure.Persistence.Repositories;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Tests;

public sealed class TicketProviderTests
{
    private const string OwnerId = "owner-1";

    private static TicketDraft Draft(TicketPriority priority = TicketPriority.High) => new()
    {
        Title = "VPN bağlantısı kurulmuyor",
        Description = "Dün akşamdan beri VPN'e bağlanamıyorum.",
        Type = "Hata",
        Priority = priority,
        DueDate = "2026-10-21",
        Labels = new List<string> { "vpn" },
        SubTasks = new List<TicketSubtask> { new() { Title = "Hata kodunu not al", Description = "691" } },
        Confidence = 90
    };

    [Fact]
    public async Task Trello_publishes_card_with_credentials_list_and_due_date()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Trello = new TrelloProviderSettings { Key = "key-1", Token = "token-1" }
        }, _ => Json(new { id = "card123", url = "https://trello.com/c/card123" }));

        var result = await harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Trello, "5F1A2B3C4D5E6F7890ABCDEF", Draft());

        Assert.Equal(new PublishResult("card123", "https://trello.com/c/card123"), result);
        var call = Assert.Single(harness.Http.Calls);
        Assert.Equal("POST", call.Method);
        var url = new Uri(call.Url);
        Assert.Equal("https://api.trello.com/1/cards", url.GetLeftPart(UriPartial.Path));
        var query = ParseQuery(url);
        Assert.Equal("key-1", query["key"]);
        Assert.Equal("token-1", query["token"]);
        Assert.Equal("5f1a2b3c4d5e6f7890abcdef", query["idList"]);
        Assert.Equal("VPN bağlantısı kurulmuyor", query["name"]);
        Assert.Equal("2026-10-21T17:00:00.000Z", query["due"]);
        Assert.Contains("Öncelik: Yüksek", query["desc"]);
        Assert.Contains("Hata kodunu not al: 691", query["desc"]);
    }

    [Fact]
    public async Task GitHub_creates_issue_with_api_version_header()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Github = new GithubProviderSettings { Token = "gh-token" }
        }, _ => Json(new { number = 42, html_url = "https://github.com/acme/destek/issues/42" }));

        var result = await harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Github, "acme/destek", Draft());

        Assert.Equal(new PublishResult("42", "https://github.com/acme/destek/issues/42"), result);
        var call = Assert.Single(harness.Http.Calls);
        Assert.Equal("https://api.github.com/repos/acme/destek/issues", call.Url);
        Assert.Equal("Bearer gh-token", call.Headers["Authorization"]);
        Assert.Equal("2022-11-28", call.Headers["X-GitHub-Api-Version"]);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal("VPN bağlantısı kurulmuyor", body.RootElement.GetProperty("title").GetString());
        Assert.Contains("--- Ticket bilgileri ---", body.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task GitHub_enterprise_keeps_custom_api_prefix()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Github = new GithubProviderSettings { Token = "gh-token", BaseUrl = "https://ghe.acme.com/api/v3/" }
        }, _ => Json(new { number = 7, html_url = "https://ghe.acme.com/acme/destek/issues/7" }));

        await harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Github, "acme/destek", Draft());

        Assert.Equal("https://ghe.acme.com/api/v3/repos/acme/destek/issues", Assert.Single(harness.Http.Calls).Url);
    }

    [Fact]
    public async Task Asana_creates_task_with_date_only_due_date()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Asana = new AsanaProviderSettings { Token = "asana-token" }
        }, _ => Json(new
        {
            data = new { gid = "120999", permalink_url = "https://app.asana.com/0/120999/120999" }
        }));

        var result = await harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Asana, "1201234567890123", Draft());

        Assert.Equal(new PublishResult("120999", "https://app.asana.com/0/120999/120999"), result);
        var call = Assert.Single(harness.Http.Calls);
        Assert.Equal("https://app.asana.com/api/1.0/tasks?opt_fields=permalink_url", call.Url);
        Assert.Equal("Bearer asana-token", call.Headers["Authorization"]);
        using var body = JsonDocument.Parse(call.Body!);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("VPN bağlantısı kurulmuyor", data.GetProperty("name").GetString());
        Assert.Equal("1201234567890123", Assert.Single(data.GetProperty("projects").EnumerateArray()).GetString());
        Assert.Equal("2026-10-21", data.GetProperty("due_on").GetString());
        Assert.Contains("Ticket bilgileri", data.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task ClickUp_maps_priority_and_due_date_to_task_payload()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Clickup = new ClickupProviderSettings { Token = "cu-token" }
        }, _ => Json(new { id = "cu-1", url = "https://app.clickup.com/t/cu-1" }));

        var result = await harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Clickup, "901234567", Draft(TicketPriority.Critical));

        Assert.Equal(new PublishResult("cu-1", "https://app.clickup.com/t/cu-1"), result);
        var call = Assert.Single(harness.Http.Calls);
        Assert.Equal("https://api.clickup.com/api/v2/list/901234567/task", call.Url);
        Assert.Equal("cu-token", call.Headers["Authorization"]);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal(1, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-10-21T17:00:00Z").ToUnixTimeMilliseconds(), body.RootElement.GetProperty("due_date").GetInt64());
        Assert.Equal("VPN bağlantısı kurulmuyor", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Provider_errors_include_status_and_provider_message()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Github = new GithubProviderSettings { Token = "bad" }
        }, _ => Json(new { message = "Bad credentials" }, HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Github, "acme/destek", Draft()));

        Assert.Contains("GitHub Issues isteği başarısız (401", error.Message);
        Assert.Contains("Bad credentials", error.Message);
    }

    [Fact]
    public async Task Publishing_requires_credentials_and_valid_destination()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings(), _ => Json(new { }));

        var missing = await Assert.ThrowsAsync<ApiException>(() =>
            harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Asana, "1201234567890123", Draft()));
        Assert.Contains("bağlantı bilgileri", missing.Message);
        Assert.Contains("eksik", missing.Message);

        var invalid = await Assert.ThrowsAsync<ApiException>(() =>
            harness.Publisher.PublishAsync(OwnerId, "ticket-1", TicketTarget.Clickup, "abc", Draft()));
        Assert.Contains("sayısal", invalid.Message);
    }

    [Fact]
    public async Task Trello_probe_checks_account_and_target_list()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Trello = new TrelloProviderSettings { Key = "key-1", Token = "token-1" }
        }, url => url.Contains("/members/me", StringComparison.Ordinal)
            ? Json(new { id = "member-1", username = "ayse" })
            : Json(new { id = "5f1a2b3c4d5e6f7890abcdef", name = "Destek", closed = false }));

        var result = await harness.Probe.ProbeAsync(OwnerId, TicketTarget.Trello, "5F1A2B3C4D5E6F7890ABCDEF");

        Assert.Equal(new ProbeResult("ayse", true, "Destek"), result);
        Assert.Equal(2, harness.Http.Calls.Count);
        Assert.Contains("/members/me?", harness.Http.Calls[0].Url);
        Assert.Contains("/lists/5f1a2b3c4d5e6f7890abcdef?", harness.Http.Calls[1].Url);
    }

    [Fact]
    public async Task GitHub_probe_rejects_repository_with_issues_disabled()
    {
        await using var harness = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Github = new GithubProviderSettings { Token = "gh" }
        }, url => url.EndsWith("/user", StringComparison.Ordinal)
            ? Json(new { login = "ayse" })
            : Json(new { full_name = "acme/destek", has_issues = false }));

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            harness.Probe.ProbeAsync(OwnerId, TicketTarget.Github, "acme/destek"));

        Assert.Contains("Issues sekmesi kapalı", error.Message);
    }

    [Fact]
    public async Task Asana_and_ClickUp_probes_verify_destination()
    {
        await using var asana = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Asana = new AsanaProviderSettings { Token = "a" }
        }, url => url.Contains("/users/me", StringComparison.Ordinal)
            ? Json(new { data = new { name = "Ayşe", gid = "1" } })
            : Json(new { data = new { name = "Destek", archived = false } }));

        Assert.Equal(new ProbeResult("Ayşe", true, "Destek"),
            await asana.Probe.ProbeAsync(OwnerId, TicketTarget.Asana, "1201234567890123"));

        await using var clickup = await TicketApiHarness.CreateAsync(new ProviderSettings
        {
            Clickup = new ClickupProviderSettings { Token = "c" }
        }, url => url.EndsWith("/user", StringComparison.Ordinal)
            ? Json(new { user = new { username = "ayse", id = 5 } })
            : Json(new { name = "Destek", archived = false }));

        Assert.Equal(new ProbeResult("ayse", true, "Destek"),
            await clickup.Probe.ProbeAsync(OwnerId, TicketTarget.Clickup, "901234567"));
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return result;
    }

    private sealed class TicketApiHarness : IAsyncDisposable
    {
        private readonly SqliteTestDatabase _database;

        private TicketApiHarness(SqliteTestDatabase database, StubHttpMessageHandler http, TicketPublisher publisher, TicketProviderProbe probe)
        {
            _database = database;
            Http = http;
            Publisher = publisher;
            Probe = probe;
        }

        public StubHttpMessageHandler Http { get; }
        public TicketPublisher Publisher { get; }
        public TicketProviderProbe Probe { get; }

        public static async Task<TicketApiHarness> CreateAsync(ProviderSettings providers, Func<string, HttpResponseMessage> responder)
        {
            var database = await SqliteTestDatabase.CreateAsync();
            var settings = new SettingsService(new InMemorySettingRepository());
            await settings.SetAsync(OwnerId, "providers", providers);
            var http = new StubHttpMessageHandler(responder);
            var client = new HttpClient(http);
            return new TicketApiHarness(
                database,
                http,
                new TicketPublisher(settings, new TicketRepository(database.Db), new UnitOfWork(database.Db), new TestClock(), client),
                new TicketProviderProbe(settings, client));
        }

        public async ValueTask DisposeAsync() => await _database.DisposeAsync();
    }

    private sealed class StubHttpMessageHandler(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpCall> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.Ordinal);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new HttpCall(request.Method.Method, url, headers, body));
            return responder(url);
        }
    }

    private sealed record HttpCall(string Method, string Url, Dictionary<string, string> Headers, string? Body);
}
