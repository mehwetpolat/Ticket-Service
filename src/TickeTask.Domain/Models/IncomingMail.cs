namespace TickeTask.Domain.Models;

public class IncomingMail
{
    public string Mailbox { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> AttachmentNames { get; set; } = new();
    public bool AutoSubmitted { get; set; }
    public bool Bulk { get; set; }
    public bool ListUnsubscribe { get; set; }
}

public class MailProcessResult
{
    public string Status { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string? ExternalId { get; set; }
    public string? Error { get; set; }
}
