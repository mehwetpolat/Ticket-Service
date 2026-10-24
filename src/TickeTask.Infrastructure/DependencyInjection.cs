using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Tickets;
using TickeTask.Infrastructure.Integrations;
using TickeTask.Infrastructure.Mail;
using TickeTask.Infrastructure.Persistence;
using TickeTask.Infrastructure.Persistence.Repositories;
using TickeTask.Infrastructure.Security;
using TickeTask.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace TickeTask.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTickeTaskInfrastructure(this IServiceCollection services, AppRuntime runtime, string databasePath)
    {
        services.AddSingleton(runtime);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, IdGenerator>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<IRateLimiter, RateLimiter>();
        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<ITicketTargetCatalog, TicketTargetCatalog>();

        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IMailSenderRepository, MailSenderRepository>();
        services.AddScoped<IMailIngestionRepository, MailIngestionRepository>();
        services.AddScoped<IIntegrationLogRepository, IntegrationLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IMailConfigService, MailConfigService>();
        services.AddScoped<IMailSenderService, MailSenderService>();
        services.AddScoped<ILocalLlmClient, LocalLlmClient>();
        services.AddScoped<IMessageIngestionService, MessageIngestionService>();
        services.AddScoped<IMailIngestionService, MailIngestionService>();
        services.AddScoped<ISetupStatusService, SetupStatusService>();
        if (string.IsNullOrWhiteSpace(runtime.ModelControlUrl))
            services.AddSingleton<IModelControlClient, LocalModelStorageService>();
        else
            services.AddSingleton<IModelControlClient, ModelControlClient>();
        services.AddSingleton<IModelFolderPicker, ModelFolderPicker>();
        services.AddScoped<TicketPublisher>();
        services.AddScoped<ITicketPublisher>(provider => provider.GetRequiredService<TicketPublisher>());
        services.AddScoped<ITicketProviderProbe, TicketProviderProbe>();
        services.AddHttpClient();
        services.AddSingleton<ImapMailProvider>();
        services.AddSingleton<GraphMailProvider>();
        services.AddSingleton<GmailMailProvider>();
        services.AddSingleton<IMailProvider>(provider => provider.GetRequiredService<ImapMailProvider>());
        services.AddSingleton<IMailProvider>(provider => provider.GetRequiredService<GraphMailProvider>());
        services.AddSingleton<IMailProvider>(provider => provider.GetRequiredService<GmailMailProvider>());
        services.AddHostedService<MailPoller>();
        return services;
    }
}

public static class RuntimeBootstrap
{
    public static AppRuntime Create(string contentRoot)
    {
        var runtimeRoot = Environment.GetEnvironmentVariable("TICKETASK_RUNTIME_ROOT");
        if (string.IsNullOrWhiteSpace(runtimeRoot)) runtimeRoot = Path.Combine(contentRoot, ".ticketask-runtime");
        Directory.CreateDirectory(runtimeRoot);

        var runtime = new AppRuntime
        {
            LocalMode = Environment.GetEnvironmentVariable("APP_LOCAL_MODE") != "0",
            AllowSelfRegistration = Environment.GetEnvironmentVariable("ALLOW_SELF_REGISTRATION") != "0",
            PublicOrigin = NullIfEmpty(Environment.GetEnvironmentVariable("APP_PUBLIC_ORIGIN")),
            TrustProxy = Environment.GetEnvironmentVariable("TRUST_PROXY") == "1",
            MasterKey = ResolveSecret(Environment.GetEnvironmentVariable("APP_MASTER_KEY"), runtimeRoot, "master-key", 32),
            AccessPassword = ResolveSecret(Environment.GetEnvironmentVariable("APP_ACCESS_PASSWORD"), runtimeRoot, "admin-password", 12),
            MailIngestToken = ResolveSecret(Environment.GetEnvironmentVariable("MAIL_INGEST_TOKEN"), runtimeRoot, "mail-ingest-token", 32),
            InboundWebhookToken = Environment.GetEnvironmentVariable("INBOUND_WEBHOOK_TOKEN") ?? string.Empty,
            WhatsAppVerifyToken = Environment.GetEnvironmentVariable("WHATSAPP_VERIFY_TOKEN") ?? string.Empty,
            WhatsAppAppSecret = Environment.GetEnvironmentVariable("WHATSAPP_APP_SECRET") ?? string.Empty,
            WebhookOwnerId = Environment.GetEnvironmentVariable("WEBHOOK_OWNER_ID") ?? "local-user",
            AllowedSenders = Environment.GetEnvironmentVariable("ALLOWED_SENDERS") ?? string.Empty,
            AllowedDomains = Environment.GetEnvironmentVariable("ALLOWED_DOMAINS") ?? string.Empty,
            ModelControlUrl = Environment.GetEnvironmentVariable("MODEL_CONTROL_URL") ?? string.Empty,
            ModelOllamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://127.0.0.1:11434",
            ModelConfigDirectory = Environment.GetEnvironmentVariable("MODEL_CONFIG_DIR") ?? Path.Combine(contentRoot, "data", "model-config"),
            ModelPrimaryDirectory = Environment.GetEnvironmentVariable("MODEL_PRIMARY_DIR") ?? Path.Combine(contentRoot, "data", "model-primary"),
            ModelSecondaryDirectory = Environment.GetEnvironmentVariable("MODEL_SECONDARY_DIR") ?? Path.Combine(contentRoot, "data", "model-secondary"),
            ModelPrimaryHostPath = Environment.GetEnvironmentVariable("MODEL_PRIMARY_HOST_PATH") ?? string.Empty,
            ModelSecondaryHostPath = Environment.GetEnvironmentVariable("MODEL_SECONDARY_HOST_PATH") ?? string.Empty,
            ModelPrimaryLabel = Environment.GetEnvironmentVariable("MODEL_PRIMARY_LABEL") ?? "Birinci konum",
            ModelSecondaryLabel = Environment.GetEnvironmentVariable("MODEL_SECONDARY_LABEL") ?? "İkinci konum",
            MailStateDirectory = Environment.GetEnvironmentVariable("MAIL_STATE_DIR") ?? Path.Combine(contentRoot, "data", "mail")
        };
        Directory.CreateDirectory(runtime.MailStateDirectory);
        Directory.CreateDirectory(runtime.ModelConfigDirectory);
        Directory.CreateDirectory(runtime.ModelPrimaryDirectory);
        Directory.CreateDirectory(runtime.ModelSecondaryDirectory);
        return runtime;
    }

    public static string ResolveDatabasePath(string contentRoot, string? configured = null)
    {
        var path = configured ?? Environment.GetEnvironmentVariable("DATABASE_PATH") ?? Path.Combine(contentRoot, "data", "ticketask.db");
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return path;
    }

    private static string ResolveSecret(string? environmentValue, string directory, string name, int bytes)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue.Trim();
        var path = Path.Combine(directory, name);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0) return existing;
        }
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
        File.WriteAllText(path, value, new UTF8Encoding(false));
        return value;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
