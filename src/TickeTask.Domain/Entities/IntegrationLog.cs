using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class IntegrationLog
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string Target { get; set; } = string.Empty;
    public IntegrationLogLevel Level { get; set; } = IntegrationLogLevel.Info;
    public string Message { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
}
