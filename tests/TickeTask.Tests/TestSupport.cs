using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Tickets;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;
using TickeTask.Infrastructure.Persistence;
using TickeTask.Infrastructure.Persistence.Repositories;
using TickeTask.Infrastructure.Security;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Tests;

internal static class TestRuntime
{
    public static AppRuntime Create() => new()
    {
        LocalMode = true,
        AllowSelfRegistration = true,
        MasterKey = Convert.ToHexString(new byte[32]),
        AccessPassword = "test-setup-password",
        MailIngestToken = new string('a', 64),
        InboundWebhookToken = "test-webhook-token",
        AllowedSenders = "user@example.com",
        AllowedDomains = "example.org",
        MailStateDirectory = Path.Combine(Path.GetTempPath(), "ticketask-tests", Guid.NewGuid().ToString("N"))
    };
}

internal sealed class TestClock(long unixNow = 1_700_000_000) : IClock
{
    public long UnixNow { get; set; } = unixNow;
    public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeSeconds(UnixNow);

    public void Advance(int seconds) => UnixNow += seconds;
}

internal sealed class InMemorySettingRepository : ISettingRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> _values = new();

    public Task<T?> GetAsync<T>(string ownerId, string key, CancellationToken ct = default)
    {
        if (!_values.TryGetValue($"{ownerId}:{key}", out var json)) return Task.FromResult<T?>(default);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json, Options));
    }

    public Task SetAsync<T>(string ownerId, string key, T value, CancellationToken ct = default)
    {
        _values[$"{ownerId}:{key}"] = JsonSerializer.Serialize(value, Options);
        return Task.CompletedTask;
    }
}

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase(SqliteConnection connection, AppDbContext db, AppRuntime runtime)
    {
        _connection = connection;
        Db = db;
        Runtime = runtime;
    }

    public AppDbContext Db { get; }
    public AppRuntime Runtime { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteTestDatabase(connection, db, TestRuntime.Create());
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class FakeLlmClient : ILocalLlmClient
{
    public Task<LlmCheckResult> CheckAsync(string ownerId, CancellationToken ct = default) =>
        Task.FromResult(new LlmCheckResult(false, false, new List<string>(), "http://127.0.0.1:11434", "qwen3:4b", "offline"));

    public Task<object> AnalyzeAsync(string ownerId, string message, object schema, CancellationToken ct = default)
    {
        var automated = message.Contains("otomatik yanıt", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult<object>(new
        {
            actionable = !automated,
            reason = automated ? "Otomatik yanıt olduğu için ticket oluşturulmadı." : string.Empty,
            title = "Yazıcıdan belge çıkmıyor",
            description = "İkinci kattaki yazıcıdan belge çıkmıyor ve kullanıcılar işlerini tamamlayamıyor. Yazıcı bağlantısı ve yazdırma kuyruğu kontrol edilmeli.",
            type = "Hata",
            priority = "high",
            dueDate = string.Empty,
            labels = new[] { "yazici" },
            confidence = 90,
            questions = Array.Empty<string>(),
            subTasks = Array.Empty<object>()
        });
    }

    public Task<HttpResponseMessage> SendAsync(string ownerId, string path, HttpRequestMessage request, TimeSpan timeout, CancellationToken ct = default) =>
        throw new InvalidOperationException("offline");
}

internal sealed class RecordingPublisher : ITicketPublisher
{
    public List<(string TicketId, TicketTarget Target, string Destination, TicketDraft Draft)> Calls { get; } = new();
    public Func<string, string, PublishResult>? Handler { get; set; }
    public Exception? Failure { get; set; }

    public Task<PublishResult> PublishAsync(string ownerId, string ticketId, TicketTarget target, string destinationId, TicketDraft draft, CancellationToken ct = default)
    {
        Calls.Add((ticketId, target, destinationId, draft));
        if (Failure is not null) throw Failure;
        var result = Handler?.Invoke(ticketId, destinationId) ?? new PublishResult($"EXT-{Calls.Count}", $"https://example.test/{ticketId}");
        return Task.FromResult(result);
    }
}

internal sealed class MailIngestionHarness : IAsyncDisposable
{
    private MailIngestionHarness(
        SqliteTestDatabase database,
        IMailIngestionService ingestion,
        IMailConfigService mailConfig,
        IProjectService project,
        IMessageRepository messages,
        ITicketRepository tickets,
        IMailIngestionRepository ingestions,
        RecordingPublisher publisher,
        TestClock clock,
        AppRuntime runtime)
    {
        Database = database;
        Ingestion = ingestion;
        MailConfig = mailConfig;
        Project = project;
        Messages = messages;
        Tickets = tickets;
        Ingestions = ingestions;
        Publisher = publisher;
        Clock = clock;
        Runtime = runtime;
    }

    public SqliteTestDatabase Database { get; }
    public IMailIngestionService Ingestion { get; }
    public IMailConfigService MailConfig { get; }
    public IProjectService Project { get; }
    public IMessageRepository Messages { get; }
    public ITicketRepository Tickets { get; }
    public IMailIngestionRepository Ingestions { get; }
    public RecordingPublisher Publisher { get; }
    public TestClock Clock { get; }
    public AppRuntime Runtime { get; }

    public static async Task<MailIngestionHarness> CreateAsync()
    {
        var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Db;
        var runtime = database.Runtime;
        var clock = new TestClock();
        var ids = new IdGenerator();
        var encryption = new EncryptionService(runtime);
        var settings = new SettingRepository(db, encryption);
        var settingsService = new SettingsService(settings);
        var customers = new CustomerRepository(db);
        var messages = new MessageRepository(db);
        var people = new PersonRepository(db);
        var devices = new DeviceRepository(db);
        var tickets = new TicketRepository(db);
        var ingestions = new MailIngestionRepository(db);
        var mailConfig = new MailConfigService(settings);
        var mailSenders = new MailSenderService(new MailSenderRepository(db), settings, runtime, clock, ids);
        var project = new ProjectService(settingsService, customers);
        var publisher = new RecordingPublisher();
        var ingestion = new MailIngestionService(
            ingestions, messages, people, devices, tickets, project, mailConfig, mailSenders,
            new FakeLlmClient(), publisher, new TicketTargetCatalog(),
            new UnitOfWork(db), clock, ids);

        return new MailIngestionHarness(database, ingestion, mailConfig, project, messages, tickets, ingestions, publisher, clock, runtime);
    }

    public async Task ConfigureAsync(bool dryRun = true)
    {
        await MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            DryRun = dryRun,
            PollSeconds = 60,
            MaxPerPoll = 25,
            Mailboxes = new List<MailboxOptions>
            {
                new()
                {
                    Id = "inbox",
                    Label = "IT",
                    Host = "imap.example.com",
                    Port = 993,
                    Secure = true,
                    User = "it@example.com",
                    Password = "secret",
                    Mailbox = "INBOX"
                }
            }
        });
        await Project.SaveAsync("local-user", new ProjectConfig
        {
            Target = TicketTarget.Jira,
            DestinationId = "IT",
            Name = "Test projesi"
        });
    }

    public async ValueTask DisposeAsync() => await Database.DisposeAsync();
}
