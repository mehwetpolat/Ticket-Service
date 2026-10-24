using TickeTask.Domain.Models;

namespace TickeTask.Application.Abstractions;

public interface ILocalLlmClient
{
    Task<LlmCheckResult> CheckAsync(string ownerId, CancellationToken ct = default);
    Task<object> AnalyzeAsync(string ownerId, string message, object schema, CancellationToken ct = default);
    Task<HttpResponseMessage> SendAsync(string ownerId, string path, HttpRequestMessage request, TimeSpan timeout, CancellationToken ct = default);
}

public record LlmCheckResult(
    bool Connected,
    bool Installed,
    List<string> Models,
    string Endpoint,
    string Model,
    string? Error);

public interface ILocalAnalyzer
{
    TicketDraft Analyze(string message);
}

public interface IMailConfigService
{
    Task<MailOptions?> GetAsync(CancellationToken ct = default);
    Task<AdminMailOptions?> GetForAdminAsync(CancellationToken ct = default);
    Task SaveAsync(MailOptions input, CancellationToken ct = default);
    Task<MailPollStatus?> GetPollStatusAsync(CancellationToken ct = default);
    Task SetPollStatusAsync(MailPollStatus status, CancellationToken ct = default);
}

public interface IMailProvider
{
    MailProviderKind Provider { get; }
    Task<MailboxPollStatus> PollAsync(MailboxOptions account, MailOptions options, IMailIngestionService ingestion, CancellationToken ct = default);
}

public interface IMailSenderService
{
    Task<List<MailSenderInfo>> ListAsync(CancellationToken ct = default);
    Task<List<MailDomainInfo>> ListDomainsAsync(CancellationToken ct = default);
    Task AddDomainAsync(string domain, CancellationToken ct = default);
    Task RemoveDomainAsync(string domain, CancellationToken ct = default);
    Task<AllowedSender?> GetAllowedAsync(string email, CancellationToken ct = default);
    Task SaveAsync(string email, string name, bool active, CancellationToken ct = default);
}

public record MailSenderInfo(string? Id, string Email, string Name, bool Active, bool FromEnv);
public record MailDomainInfo(string Domain, bool FromEnv);
public record AllowedSender(string Email, string Name);

public interface IProjectService
{
    Task<ProjectConfig> GetAsync(string ownerId, CancellationToken ct = default);
    Task SaveAsync(string ownerId, ProjectConfig config, CancellationToken ct = default);
}

public interface IMailIngestionService
{
    Task<MailProcessResult> AcceptAsync(IncomingMail input, CancellationToken ct = default);
    Task<List<object>> RetryFailedAsync(int limit = 10, CancellationToken ct = default);
}
