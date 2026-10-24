using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TickeTask.Application.Abstractions;
using TickeTask.Infrastructure;

namespace TickeTask.Tests;

public sealed class TickeTaskWebFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "test-setup-password";
    public const string MailToken = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static readonly string[] EnvironmentKeys =
    {
        "TICKETASK_RUNTIME_ROOT", "DATABASE_PATH", "MAIL_STATE_DIR", "APP_MASTER_KEY", "APP_ACCESS_PASSWORD",
        "MAIL_INGEST_TOKEN", "INBOUND_WEBHOOK_TOKEN", "APP_LOCAL_MODE", "APP_PUBLIC_ORIGIN", "TRUST_PROXY",
        "ALLOWED_SENDERS", "ALLOWED_DOMAINS", "JIRA_BASE_URL", "JIRA_EMAIL", "JIRA_API_TOKEN", "GITHUB_TOKEN",
        "OLLAMA_URL"
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ticketask-api-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _original = new();

    public TickeTaskWebFactory()
    {
        foreach (var key in EnvironmentKeys) _original[key] = Environment.GetEnvironmentVariable(key);
        Directory.CreateDirectory(_root);
        Set("TICKETASK_RUNTIME_ROOT", Path.Combine(_root, "runtime"));
        Set("DATABASE_PATH", Path.Combine(_root, "ticketask.db"));
        Set("MAIL_STATE_DIR", Path.Combine(_root, "mail"));
        Set("APP_MASTER_KEY", new string('a', 64));
        Set("APP_ACCESS_PASSWORD", AdminPassword);
        Set("MAIL_INGEST_TOKEN", MailToken);
        Set("INBOUND_WEBHOOK_TOKEN", "test-webhook-token");
        Set("APP_LOCAL_MODE", "1");
        Set("APP_PUBLIC_ORIGIN", null);
        Set("TRUST_PROXY", "0");
        Set("ALLOWED_SENDERS", "user@example.com");
        Set("ALLOWED_DOMAINS", "example.org");
        Set("JIRA_BASE_URL", null);
        Set("JIRA_EMAIL", null);
        Set("JIRA_API_TOKEN", null);
        Set("GITHUB_TOKEN", null);
        Set("OLLAMA_URL", "http://127.0.0.1:1");
    }

    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.PostAsync("/api/session", Json(new { email = AdminEmail, password = AdminPassword }));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        return client;
    }

    public static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value, JsonDefaults.Options), Encoding.UTF8, "application/json");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocalLlmClient>();
            services.AddSingleton<ILocalLlmClient, FakeLlmClient>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var (key, value) in _original) Environment.SetEnvironmentVariable(key, value);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void Set(string key, string? value) => Environment.SetEnvironmentVariable(key, value);
}

[Collection("api")]
public sealed class ApiIntegrationTests : IClassFixture<TickeTaskWebFactory>
{
    private readonly TickeTaskWebFactory _factory;

    public ApiIntegrationTests(TickeTaskWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_reports_database_and_encryption_ready()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await client.GetAsync("/api/health");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("checks").GetProperty("database").GetBoolean());
        Assert.True(json.RootElement.GetProperty("checks").GetProperty("encryption").GetBoolean());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("none", response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").Single());
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Setup_login_creates_admin_session()
    {
        using var client = await _factory.CreateSignedInClientAsync();

        using var session = await client.GetAsync("/api/session");
        using var sessionJson = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        Assert.Equal(TickeTaskWebFactory.AdminEmail, sessionJson.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("admin", sessionJson.RootElement.GetProperty("user").GetProperty("role").GetString());

        using var setup = await client.GetAsync("/api/setup-status");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
    }

    [Fact]
    public async Task Directory_ticket_and_duplicate_flow()
    {
        using var client = await _factory.CreateSignedInClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var personId = await PostIdAsync(client, "/api/people", new
        {
            name = $"Test Kullanıcı {suffix}",
            email = $"person-{suffix}@example.test",
            department = "BT",
            phone = "555-0100",
            userId = (string?)null
        }, HttpStatusCode.Created);

        var deviceId = await PostIdAsync(client, "/api/devices", new
        {
            name = $"Dizüstü {suffix}",
            kind = "laptop",
            ipAddress = "10.0.0.42",
            serial = $"SN-{suffix}",
            location = "İstanbul",
            personId,
            parentId = (string?)null,
            notes = "Test cihazı"
        }, HttpStatusCode.Created);

        var customerName = $"Müşteri {suffix}";
        var customerId = await PostIdAsync(client, "/api/customers", new
        {
            name = customerName,
            email = $"customer-{suffix}@example.test",
            phone = "555-0200",
            target = "jira",
            destinationId = "IT"
        }, HttpStatusCode.Created);

        using var duplicate = await client.PostAsync("/api/customers", TickeTaskWebFactory.Json(new
        {
            name = customerName,
            email = "",
            phone = "",
            target = "jira",
            destinationId = "IT"
        }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("zaten var", await duplicate.Content.ReadAsStringAsync());

        var messageId = await PostIdAsync(client, "/api/messages", new
        {
            customerId,
            personId,
            deviceId,
            channel = "manual",
            sender = "Test Kullanıcı",
            subject = "Yazıcı arızası",
            content = "İkinci kattaki yazıcıdan belge çıkmıyor, acil destek gerekiyor.",
            externalId = (string?)null
        }, HttpStatusCode.Created);

        using var analyze = await client.PostAsync("/api/analyze", TickeTaskWebFactory.Json(new
        {
            message = "Yazıcıdan belge çıkmıyor, acil destek gerekiyor."
        }));
        Assert.Equal(HttpStatusCode.OK, analyze.StatusCode);
        using var analyzeJson = JsonDocument.Parse(await analyze.Content.ReadAsStringAsync());
        Assert.Equal("ai", analyzeJson.RootElement.GetProperty("mode").GetString());

        var ticketId = await PostIdAsync(client, "/api/tickets", new
        {
            customerId,
            personId,
            deviceId,
            messageId,
            analysisMode = "local",
            publish = false,
            draft = new
            {
                title = "Yazıcı arızası",
                description = "Yazıcıdan belge çıkmıyor, acil destek gerekiyor.",
                type = "Hata",
                priority = "high",
                dueDate = "",
                labels = new[] { "yazici" },
                confidence = 80,
                questions = Array.Empty<string>(),
                subTasks = new[] { new { title = "Kablo kontrolü", description = "Bağlantıyı doğrula" } }
            },
            attachments = Array.Empty<object>()
        }, HttpStatusCode.Created);

        using var tickets = await client.GetAsync("/api/tickets");
        using var ticketsJson = JsonDocument.Parse(await tickets.Content.ReadAsStringAsync());
        var ticket = ticketsJson.RootElement.GetProperty("tickets").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ticketId);
        Assert.Equal("draft", ticket.GetProperty("status").GetString());
        Assert.Equal("Yazıcı arızası", ticket.GetProperty("title").GetString());
        Assert.Equal("Müşteri " + suffix, ticket.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task Mail_ingestion_accepts_token_and_creates_dry_run_ticket()
    {
        using var client = await _factory.CreateSignedInClientAsync();

        using var project = await client.PostAsync("/api/project", TickeTaskWebFactory.Json(new
        {
            target = "jira",
            destinationId = "IT",
            name = "API test projesi"
        }));
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);

        using var config = await client.PostAsync("/api/mail-config", TickeTaskWebFactory.Json(new
        {
            enabled = true,
            dryRun = true,
            pollSeconds = 60,
            importExisting = false,
            maxPerPoll = 25,
            mailboxes = new[]
            {
                new { id = "inbox", label = "IT", host = "imap.example.com", port = 993, secure = true, user = "it@example.com", password = "secret", mailbox = "INBOX" }
            }
        }));
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mail-ingest")
        {
            Content = TickeTaskWebFactory.Json(new
            {
                mailbox = "IT",
                messageId = $"<api-{Guid.NewGuid():N}@example.test>",
                senderEmail = "user@example.com",
                senderName = "API Kullanıcı",
                subject = "Yazıcı arızası",
                body = "Merhaba, yazıcıdan belge çıkmıyor. Acil destek gerekiyor.\nSaygılarımla",
                attachmentNames = new[] { "ekran.png" },
                autoSubmitted = false,
                bulk = false,
                listUnsubscribe = false
            })
        };
        request.Headers.Add("x-mail-ingest-token", TickeTaskWebFactory.MailToken);

        using var response = await client.SendAsync(request);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dry_run", result.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.RootElement.GetProperty("ticketId").GetString()));

        using var list = await client.GetAsync("/api/mail-ingestions");
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listJson.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("senderEmail").GetString() == "user@example.com");
    }

    [Fact]
    public async Task Cross_origin_mutation_and_anonymous_requests_are_rejected()
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var unauthorized = await anonymous.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = await _factory.CreateSignedInClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/people")
        {
            Content = TickeTaskWebFactory.Json(new
            {
                name = "Kötü Origin",
                email = "",
                department = "",
                phone = "",
                userId = (string?)null
            })
        };
        request.Headers.Add("Origin", "http://evil.example");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Local_ticket_workflow_and_comments_are_managed_independently()
    {
        using var client = await _factory.CreateSignedInClientAsync();
        var ticketId = await PostIdAsync(client, "/api/tickets", new
        {
            analysisMode = "ai",
            publish = false,
            draft = new
            {
                title = "Yerel akış testi",
                description = "Ticket yerel olarak yönetilmeli.",
                type = "Talep",
                priority = "normal",
                dueDate = "",
                labels = new[] { "test" },
                confidence = 80,
                questions = Array.Empty<string>(),
                subTasks = Array.Empty<object>()
            },
            attachments = Array.Empty<object>()
        }, HttpStatusCode.Created);

        using var patch = await client.PatchAsync($"/api/tickets/{ticketId}", TickeTaskWebFactory.Json(new { workflowStatus = "inprogress" }));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        using var list = await client.GetAsync("/api/tickets");
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ticket = listJson.RootElement.GetProperty("tickets").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ticketId);
        Assert.Equal("inprogress", ticket.GetProperty("workflowStatus").GetString());

        using var comment = await client.PostAsync($"/api/tickets/{ticketId}/comments", TickeTaskWebFactory.Json(new { body = "Bağlantı kontrol edildi." }));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        using var comments = await client.GetAsync($"/api/tickets/{ticketId}/comments");
        using var commentsJson = JsonDocument.Parse(await comments.Content.ReadAsStringAsync());
        Assert.Single(commentsJson.RootElement.GetProperty("comments").EnumerateArray());
        Assert.Equal("Bağlantı kontrol edildi.", commentsJson.RootElement.GetProperty("comments")[0].GetProperty("body").GetString());

        using var resolve = await client.PatchAsync($"/api/tickets/{ticketId}", TickeTaskWebFactory.Json(new { workflowStatus = "resolved" }));
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        using var resolvedList = await client.GetAsync("/api/tickets");
        using var resolvedJson = JsonDocument.Parse(await resolvedList.Content.ReadAsStringAsync());
        var resolved = resolvedJson.RootElement.GetProperty("tickets").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ticketId);
        Assert.Equal("resolved", resolved.GetProperty("workflowStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, resolved.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(1, resolved.GetProperty("commentCount").GetInt32());
    }

    private static async Task<string> PostIdAsync(HttpClient client, string path, object body, HttpStatusCode expected)
    {
        using var response = await client.PostAsync(path, TickeTaskWebFactory.Json(body));
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} returned {(int)response.StatusCode}: {content}");
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetString()!;
    }
}
