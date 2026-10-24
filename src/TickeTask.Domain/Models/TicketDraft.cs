using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Models;

public class TicketSubtask
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
}

public class TicketAttachment
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Data { get; set; } = string.Empty;
    public bool Uploaded { get; set; }
}

public class TicketDraft
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Talep";
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public string DueDate { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public int Confidence { get; set; }
    public List<string> Questions { get; set; } = new();
    public List<TicketSubtask> SubTasks { get; set; } = new();
}

public class MailAnalysis : TicketDraft
{
    public bool Actionable { get; set; } = true;
    public string Reason { get; set; } = string.Empty;
}
