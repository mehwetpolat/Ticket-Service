using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class Message
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? PersonId { get; set; }
    public string? DeviceId { get; set; }
    public string? CreatedBy { get; set; }
    public MessageChannel Channel { get; set; } = MessageChannel.Manual;
    public string? Sender { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.New;
    public long ReceivedAt { get; set; }

    public Customer? Customer { get; set; }
    public Person? Person { get; set; }
    public Device? Device { get; set; }
    public User? CreatedByUser { get; set; }
}
