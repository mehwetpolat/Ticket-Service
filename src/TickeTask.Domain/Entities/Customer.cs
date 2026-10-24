using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class Customer
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public TicketTarget Target { get; set; } = TicketTarget.Jira;
    public string DestinationId { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
