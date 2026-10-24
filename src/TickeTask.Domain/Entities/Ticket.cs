using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class Ticket
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? PersonId { get; set; }
    public string? DeviceId { get; set; }
    public string? CreatedBy { get; set; }
    public string? AssignedTo { get; set; }
    public string? MessageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Talep";
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public string? DueDate { get; set; }
    public string LabelsJson { get; set; } = "[]";
    public int Confidence { get; set; }
    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Ai;
    public TicketTarget Target { get; set; } = TicketTarget.Jira;
    public string DestinationId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string SubTasksJson { get; set; } = "[]";
    public string AttachmentsJson { get; set; } = "[]";
    public TicketStatus Status { get; set; } = TicketStatus.Draft;
    public TicketWorkflowStatus WorkflowStatus { get; set; } = TicketWorkflowStatus.Open;
    public long? ResolvedAt { get; set; }
    public string? Error { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public Customer? Customer { get; set; }
    public Person? Person { get; set; }
    public Device? Device { get; set; }
    public User? CreatedByUser { get; set; }
    public User? AssignedToUser { get; set; }
    public Message? SourceMessage { get; set; }
    public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
}

public class TicketComment
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string? AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public long CreatedAt { get; set; }

    public Ticket? Ticket { get; set; }
    public User? Author { get; set; }
}
