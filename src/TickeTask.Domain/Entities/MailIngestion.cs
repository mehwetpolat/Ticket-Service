using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class MailIngestion
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Mailbox { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string AttachmentNamesJson { get; set; } = "[]";
    public bool IsAutomated { get; set; }
    public MailIngestionStatus Status { get; set; } = MailIngestionStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public string? SourceMessageId { get; set; }
    public string? TicketId { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public Ticket? Ticket { get; set; }
}
