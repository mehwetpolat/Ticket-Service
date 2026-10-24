using TickeTask.Domain.Models;

namespace TickeTask.Application.Abstractions;

public class AppRuntime
{
    public bool LocalMode { get; set; } = true;
    public bool AllowSelfRegistration { get; set; } = true;
    public string? PublicOrigin { get; set; }
    public bool TrustProxy { get; set; }
    public string MasterKey { get; set; } = string.Empty;
    public string AccessPassword { get; set; } = string.Empty;
    public string MailIngestToken { get; set; } = string.Empty;
    public string InboundWebhookToken { get; set; } = string.Empty;
    public string WhatsAppVerifyToken { get; set; } = string.Empty;
    public string WhatsAppAppSecret { get; set; } = string.Empty;
    public string WebhookOwnerId { get; set; } = "local-user";
    public string AllowedSenders { get; set; } = string.Empty;
    public string AllowedDomains { get; set; } = string.Empty;
    public string ModelControlUrl { get; set; } = string.Empty;
    public string ModelOllamaUrl { get; set; } = "http://127.0.0.1:11434";
    public string ModelConfigDirectory { get; set; } = "model-config";
    public string ModelPrimaryDirectory { get; set; } = "model-primary";
    public string ModelSecondaryDirectory { get; set; } = "model-secondary";
    public string ModelPrimaryHostPath { get; set; } = string.Empty;
    public string ModelSecondaryHostPath { get; set; } = string.Empty;
    public string ModelPrimaryLabel { get; set; } = "Birinci konum";
    public string ModelSecondaryLabel { get; set; } = "İkinci konum";
    public string MailStateDirectory { get; set; } = "mail-state";

    public IReadOnlySet<string> SenderEmails() => Split(AllowedSenders);
    public IReadOnlySet<string> SenderDomains() => Split(AllowedDomains.Replace("@", string.Empty));

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string? Env(string name) => Environment.GetEnvironmentVariable(name);
}

public interface IClock
{
    long UnixNow { get; }
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    string Create(string prefix);
}

public interface IPasswordService
{
    Task<bool> VerifyAsync(string password, string salt, string hash);
    Task<(string Salt, string Hash)> CreateAsync(string password);
    Task<bool> VerifyDummyAsync(string password);
    Task<bool> ValidSetupPasswordAsync(string password);
}

public interface ISettingsService
{
    Task<T?> GetAsync<T>(string ownerId, string key, CancellationToken ct = default);
    Task SetAsync<T>(string ownerId, string key, T value, CancellationToken ct = default);
    Task<ProviderSettings> GetProvidersAsync(string ownerId, CancellationToken ct = default);
    Task<LlmSettings> GetLlmAsync(string ownerId, CancellationToken ct = default);
    Task SaveProviderAsync(string ownerId, string provider, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
    Task RemoveProviderAsync(string ownerId, string provider, CancellationToken ct = default);
    Task SaveLlmAsync(string ownerId, LlmSettings settings, CancellationToken ct = default);
}
