using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;

namespace TickeTask.Application.Abstractions.Persistence;

public record ActorRecord(string Id, string Name, string Email, UserRole Role);

public record UserView(
    string Id,
    string Name,
    string Email,
    UserRole Role,
    UserStatus Status,
    long CreatedAt,
    long? ApprovedAt,
    string? PersonId);

public record MessageView(
    string Id,
    string? CustomerId,
    string? CustomerName,
    string? PersonId,
    string? PersonName,
    string? DeviceId,
    string? DeviceName,
    string? CreatedBy,
    string? CreatedByName,
    MessageChannel Channel,
    string? Sender,
    string? Subject,
    string Content,
    MessageStatus Status,
    long ReceivedAt);

public record TicketView(
    string Id,
    string? MessageId,
    string Title,
    string Description,
    string Type,
    TicketPriority Priority,
    string? DueDate,
    string LabelsJson,
    string SubTasksJson,
    TicketTarget Target,
    string DestinationId,
    string? ExternalId,
    string? ExternalUrl,
    TicketStatus Status,
    TicketWorkflowStatus WorkflowStatus,
    string? AssignedTo,
    string? AssignedToName,
    long? ResolvedAt,
    int CommentCount,
    string? Error,
    long CreatedAt,
    string? CustomerName,
    string? PersonId,
    string? PersonName,
    string? DeviceId,
    string? DeviceName,
    string? CreatedBy,
    string? CreatedByName);

public record TicketCommentView(
    string Id,
    string TicketId,
    string? AuthorId,
    string? AuthorName,
    string Body,
    long CreatedAt);

public record MailIngestionView(
    string Id,
    string Mailbox,
    string MessageId,
    string SenderEmail,
    string SenderName,
    string Subject,
    string Status,
    int Attempts,
    string? Error,
    string? TicketId,
    long CreatedAt,
    long UpdatedAt,
    string? ExternalId,
    string? ExternalUrl,
    string? Target);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);
    Task<List<UserView>> ListAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task AddAsync(Session session, CancellationToken ct = default);
    Task<ActorRecord?> GetActorAsync(string tokenHash, long now, CancellationToken ct = default);
    Task DeleteExpiredAsync(long now, CancellationToken ct = default);
    Task DeleteByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task DeleteByUserIdAsync(string userId, CancellationToken ct = default);
    Task DeleteByUserExceptTokenAsync(string userId, string tokenHash, CancellationToken ct = default);
    Task TrimAsync(string userId, int keep, CancellationToken ct = default);
}

public interface IPersonRepository
{
    Task<Person?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Person?> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task<Person?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<Person>> ListAsync(bool admin, string? userId, CancellationToken ct = default);
    Task AddAsync(Person person, CancellationToken ct = default);
}

public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<string?> GetParentIdAsync(string id, CancellationToken ct = default);
    Task<List<Device>> ListAsync(bool admin, string? userId, CancellationToken ct = default);
    Task<List<Device>> ListByPersonAsync(string personId, CancellationToken ct = default);
    Task AddAsync(Device device, CancellationToken ct = default);
}

public interface ICustomerRepository
{
    Task<List<Customer>> ListAsync(string ownerId, CancellationToken ct = default);
    Task<Customer?> GetAsync(string id, string ownerId, CancellationToken ct = default);
    Task<Customer?> FindByNameAsync(string ownerId, string name, CancellationToken ct = default);
    Task<Customer?> FindForIngestAsync(string ownerId, string email, string phone, string name, CancellationToken ct = default);
    Task<int> CountRelatedAsync(string id, string ownerId, CancellationToken ct = default);
    Task AddAsync(Customer customer, CancellationToken ct = default);
    Task RemoveAsync(Customer customer, CancellationToken ct = default);
}

public interface IMessageRepository
{
    Task<List<MessageView>> ListAsync(string ownerId, bool admin, string? actorId, CancellationToken ct = default);
    Task<Message?> GetAsync(string id, string ownerId, CancellationToken ct = default);
    Task<Message?> GetByExternalIdAsync(string ownerId, string externalId, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
}

public interface ITicketRepository
{
    Task<List<TicketView>> ListAsync(string ownerId, bool admin, string? actorId, CancellationToken ct = default);
    Task<Ticket?> GetAsync(string id, string ownerId, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, string ownerId, CancellationToken ct = default);
    Task AddAsync(Ticket ticket, CancellationToken ct = default);
    Task<int> CountPublishedAsync(string ownerId, CancellationToken ct = default);
    Task<List<TicketCommentView>> ListCommentsAsync(string ticketId, string ownerId, CancellationToken ct = default);
    Task AddCommentAsync(TicketComment comment, CancellationToken ct = default);
}

public interface ISettingRepository
{
    Task<T?> GetAsync<T>(string ownerId, string key, CancellationToken ct = default);
    Task SetAsync<T>(string ownerId, string key, T value, CancellationToken ct = default);
}

public interface IMailSenderRepository
{
    Task<List<MailSender>> ListAsync(CancellationToken ct = default);
    Task<MailSender?> GetAsync(string email, CancellationToken ct = default);
    Task UpsertAsync(MailSender sender, CancellationToken ct = default);
}

public interface IMailIngestionRepository
{
    Task<MailIngestion?> FindDuplicateAsync(string ownerId, string messageId, CancellationToken ct = default);
    Task<MailIngestion?> FindIdentityAsync(string ownerId, string mailbox, string messageId, CancellationToken ct = default);
    Task<MailIngestion?> GetAsync(string id, CancellationToken ct = default);
    Task<List<MailIngestion>> ListRetryableAsync(long leaseThreshold, long nowUnix, CancellationToken ct = default);
    Task<bool> ClaimAsync(string id, long nowUnix, long leaseThreshold, CancellationToken ct = default);
    Task AddAsync(MailIngestion ingestion, CancellationToken ct = default);
    Task<List<MailIngestionView>> ListAsync(int limit, CancellationToken ct = default);
    Task<Dictionary<string, int>> StatsSinceAsync(long sinceUnix, CancellationToken ct = default);
    Task<Dictionary<string, int>> StatsAsync(CancellationToken ct = default);
}

public interface IIntegrationLogRepository
{
    Task AddAsync(IntegrationLog log, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
