using Microsoft.EntityFrameworkCore;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Domain.Entities;
using TickeTask.Infrastructure.Persistence;

namespace TickeTask.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(user => user.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(user => user.Email == email, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Users.CountAsync(ct);

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default) =>
        db.Users.CountAsync(user => user.Role == Domain.Enums.UserRole.Admin && user.Status == Domain.Enums.UserStatus.Active, ct);

    public async Task<List<UserView>> ListAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking()
            .OrderBy(user => user.Status == Domain.Enums.UserStatus.Pending ? 0 : 1)
            .ThenByDescending(user => user.CreatedAt)
            .Select(user => new UserView(
                user.Id, user.Name, user.Email, user.Role, user.Status, user.CreatedAt, user.ApprovedAt,
                db.People.Where(person => person.UserId == user.Id).Select(person => person.Id).FirstOrDefault()))
            .ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SessionRepository(AppDbContext db) : ISessionRepository
{
    public async Task AddAsync(Session session, CancellationToken ct = default)
    {
        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ActorRecord?> GetActorAsync(string tokenHash, long now, CancellationToken ct = default) =>
        await db.Sessions.AsNoTracking()
            .Where(session => session.TokenHash == tokenHash && session.ExpiresAt > now && session.User.Status == Domain.Enums.UserStatus.Active)
            .Select(session => new ActorRecord(session.User.Id, session.User.Name, session.User.Email, session.User.Role))
            .FirstOrDefaultAsync(ct);

    public Task DeleteExpiredAsync(long now, CancellationToken ct = default) =>
        db.Sessions.Where(session => session.ExpiresAt <= now).ExecuteDeleteAsync(ct);

    public Task DeleteByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.Sessions.Where(session => session.TokenHash == tokenHash).ExecuteDeleteAsync(ct);

    public Task DeleteByUserIdAsync(string userId, CancellationToken ct = default) =>
        db.Sessions.Where(session => session.UserId == userId).ExecuteDeleteAsync(ct);

    public Task DeleteByUserExceptTokenAsync(string userId, string tokenHash, CancellationToken ct = default) =>
        db.Sessions.Where(session => session.UserId == userId && session.TokenHash != tokenHash).ExecuteDeleteAsync(ct);

    public async Task TrimAsync(string userId, int keep, CancellationToken ct = default)
    {
        var ids = await db.Sessions.Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt).Skip(keep).Select(session => session.Id).ToListAsync(ct);
        if (ids.Count == 0) return;
        await db.Sessions.Where(session => ids.Contains(session.Id)).ExecuteDeleteAsync(ct);
    }
}

public sealed class PersonRepository(AppDbContext db) : IPersonRepository
{
    public Task<Person?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.People.FirstOrDefaultAsync(person => person.Id == id, ct);

    public Task<Person?> GetByUserIdAsync(string userId, CancellationToken ct = default) =>
        db.People.FirstOrDefaultAsync(person => person.UserId == userId, ct);

    public Task<Person?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.People.FirstOrDefaultAsync(person => person.Email != null && person.Email.ToLower() == email.ToLower(), ct);

    public async Task<List<Person>> ListAsync(bool admin, string? userId, CancellationToken ct = default)
    {
        var query = db.People.AsNoTracking();
        if (!admin) query = query.Where(person => person.UserId == userId);
        return await query.OrderBy(person => person.Name).ToListAsync(ct);
    }

    public async Task AddAsync(Person person, CancellationToken ct = default)
    {
        await db.People.AddAsync(person, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DeviceRepository(AppDbContext db) : IDeviceRepository
{
    public Task<Device?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.Devices.FirstOrDefaultAsync(device => device.Id == id, ct);

    public Task<string?> GetParentIdAsync(string id, CancellationToken ct = default) =>
        db.Devices.Where(device => device.Id == id).Select(device => device.ParentId).FirstOrDefaultAsync(ct);

    public async Task<List<Device>> ListAsync(bool admin, string? userId, CancellationToken ct = default)
    {
        var query = db.Devices.AsNoTracking().Include(device => device.Person).AsQueryable();
        if (!admin) query = query.Where(device => device.Person != null && device.Person.UserId == userId);
        return await query.OrderBy(device => device.Name).ToListAsync(ct);
    }

    public async Task<List<Device>> ListByPersonAsync(string personId, CancellationToken ct = default) =>
        await db.Devices.AsNoTracking().Where(device => device.PersonId == personId)
            .OrderBy(device => device.Name).ToListAsync(ct);

    public async Task AddAsync(Device device, CancellationToken ct = default)
    {
        await db.Devices.AddAsync(device, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class CustomerRepository(AppDbContext db) : ICustomerRepository
{
    public Task<List<Customer>> ListAsync(string ownerId, CancellationToken ct = default) =>
        db.Customers.AsNoTracking().Where(customer => customer.OwnerId == ownerId).OrderBy(customer => customer.Name).ToListAsync(ct);

    public Task<Customer?> GetAsync(string id, string ownerId, CancellationToken ct = default) =>
        db.Customers.FirstOrDefaultAsync(customer => customer.Id == id && customer.OwnerId == ownerId, ct);

    public Task<Customer?> FindByNameAsync(string ownerId, string name, CancellationToken ct = default)
    {
        var normalized = name.ToLower();
        return db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(customer => customer.OwnerId == ownerId && customer.Name.ToLower() == normalized, ct);
    }

    public Task<Customer?> FindForIngestAsync(string ownerId, string email, string phone, string name, CancellationToken ct = default) =>
        db.Customers.AsNoTracking().Where(customer => customer.OwnerId == ownerId &&
                ((customer.Email != null && customer.Email == email) ||
                 (customer.Phone != null && customer.Phone == phone) ||
                 customer.Name == name))
            .OrderBy(customer => customer.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<int> CountRelatedAsync(string id, string ownerId, CancellationToken ct = default)
    {
        var messages = await db.Messages.CountAsync(message => message.CustomerId == id && message.OwnerId == ownerId, ct);
        var tickets = await db.Tickets.CountAsync(ticket => ticket.CustomerId == id && ticket.OwnerId == ownerId, ct);
        return messages + tickets;
    }

    public async Task AddAsync(Customer customer, CancellationToken ct = default)
    {
        await db.Customers.AddAsync(customer, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Customer customer, CancellationToken ct = default)
    {
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class MessageRepository(AppDbContext db) : IMessageRepository
{
    public async Task<List<MessageView>> ListAsync(string ownerId, bool admin, string? actorId, CancellationToken ct = default)
    {
        var query = db.Messages.AsNoTracking().Where(message => message.OwnerId == ownerId);
        if (!admin) query = query.Where(message => message.CreatedBy == actorId);
        return await query.OrderByDescending(message => message.ReceivedAt)
            .Select(message => new MessageView(
                message.Id, message.CustomerId, message.Customer != null ? message.Customer.Name : null,
                message.PersonId, message.Person != null ? message.Person.Name : null,
                message.DeviceId, message.Device != null ? message.Device.Name : null,
                message.CreatedBy, message.CreatedByUser != null ? message.CreatedByUser.Name : null,
                message.Channel, message.Sender, message.Subject, message.Content, message.Status, message.ReceivedAt))
            .ToListAsync(ct);
    }

    public Task<Message?> GetAsync(string id, string ownerId, CancellationToken ct = default) =>
        db.Messages.FirstOrDefaultAsync(message => message.Id == id && message.OwnerId == ownerId, ct);

    public Task<Message?> GetByExternalIdAsync(string ownerId, string externalId, CancellationToken ct = default) =>
        db.Messages.AsNoTracking().FirstOrDefaultAsync(message => message.OwnerId == ownerId && message.ExternalId == externalId, ct);

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await db.Messages.AddAsync(message, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class TicketRepository(AppDbContext db) : ITicketRepository
{
    public async Task<List<TicketView>> ListAsync(string ownerId, bool admin, string? actorId, CancellationToken ct = default)
    {
        var query = db.Tickets.AsNoTracking().Where(ticket => ticket.OwnerId == ownerId);
        if (!admin) query = query.Where(ticket => ticket.CreatedBy == actorId || ticket.AssignedTo == actorId);
        return await query.OrderByDescending(ticket => ticket.CreatedAt)
            .Select(ticket => new TicketView(
                ticket.Id, ticket.MessageId, ticket.Title, ticket.Description, ticket.Type, ticket.Priority,
                ticket.DueDate, ticket.LabelsJson, ticket.SubTasksJson, ticket.Target, ticket.DestinationId,
                ticket.ExternalId, ticket.ExternalUrl, ticket.Status, ticket.WorkflowStatus,
                ticket.AssignedTo, ticket.AssignedToUser != null ? ticket.AssignedToUser.Name : null,
                ticket.ResolvedAt, ticket.Comments.Count, ticket.Error, ticket.CreatedAt,
                ticket.Customer != null ? ticket.Customer.Name : null,
                ticket.PersonId, ticket.Person != null ? ticket.Person.Name : null,
                ticket.DeviceId, ticket.Device != null ? ticket.Device.Name : null,
                ticket.CreatedBy, ticket.CreatedByUser != null ? ticket.CreatedByUser.Name : null))
            .ToListAsync(ct);
    }

    public Task<Ticket?> GetAsync(string id, string ownerId, CancellationToken ct = default) =>
        db.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id && ticket.OwnerId == ownerId, ct);

    public Task<bool> ExistsAsync(string id, string ownerId, CancellationToken ct = default) =>
        db.Tickets.AnyAsync(ticket => ticket.Id == id && ticket.OwnerId == ownerId, ct);

    public async Task AddAsync(Ticket ticket, CancellationToken ct = default)
    {
        await db.Tickets.AddAsync(ticket, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<int> CountPublishedAsync(string ownerId, CancellationToken ct = default) =>
        db.Tickets.CountAsync(ticket => ticket.OwnerId == ownerId && ticket.Status == Domain.Enums.TicketStatus.Published, ct);

    public async Task<List<TicketCommentView>> ListCommentsAsync(string ticketId, string ownerId, CancellationToken ct = default) =>
        await db.TicketComments.AsNoTracking()
            .Where(comment => comment.TicketId == ticketId && comment.OwnerId == ownerId)
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new TicketCommentView(
                comment.Id, comment.TicketId, comment.AuthorId,
                comment.Author != null ? comment.Author.Name : null,
                comment.Body, comment.CreatedAt))
            .ToListAsync(ct);

    public async Task AddCommentAsync(TicketComment comment, CancellationToken ct = default)
    {
        await db.TicketComments.AddAsync(comment, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class IntegrationLogRepository(AppDbContext db) : IIntegrationLogRepository
{
    public async Task AddAsync(IntegrationLog log, CancellationToken ct = default)
    {
        await db.IntegrationLogs.AddAsync(log, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
