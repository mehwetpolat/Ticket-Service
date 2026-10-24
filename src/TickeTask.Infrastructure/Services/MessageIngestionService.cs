using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;

namespace TickeTask.Infrastructure.Services;

public sealed class MessageIngestionService(
    ICustomerRepository customers,
    IMessageRepository messages,
    IClock clock,
    IIdGenerator ids) : IMessageIngestionService
{
    public async Task<(string Id, bool Duplicate)> IngestAsync(
        string ownerId,
        string? customerName,
        string? email,
        string? phone,
        MessageChannel channel,
        string? sender,
        string? subject,
        string content,
        string? externalId,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            var existing = await messages.GetByExternalIdAsync(ownerId, externalId, ct);
            if (existing is not null) return (existing.Id, true);
        }

        var name = customerName ?? sender ?? string.Empty;
        var customer = await customers.FindForIngestAsync(ownerId, email ?? string.Empty, phone ?? string.Empty, name, ct);
        if (customer is null && name.Length > 0)
        {
            var now = clock.UnixNow;
            var customerId = ids.Create("cus");
            await customers.AddAsync(new Customer
            {
                Id = customerId,
                OwnerId = ownerId,
                Name = name,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                Target = TicketTarget.Jira,
                DestinationId = string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            }, ct);
            customer = await customers.GetAsync(customerId, ownerId, ct);
        }

        var messageId = ids.Create("msg");
        await messages.AddAsync(new Message
        {
            Id = messageId,
            OwnerId = ownerId,
            CustomerId = customer?.Id,
            Channel = channel,
            Sender = string.IsNullOrWhiteSpace(sender) ? null : sender,
            Subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
            Content = content,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId,
            Status = MessageStatus.New,
            ReceivedAt = clock.UnixNow
        }, ct);
        return (messageId, false);
    }
}
