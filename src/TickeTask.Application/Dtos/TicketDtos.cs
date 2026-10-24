using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Dtos;

public record SaveMessageRequest(
    string? CustomerId,
    string? PersonId,
    string? DeviceId,
    MessageChannel Channel,
    string? Sender,
    string? Subject,
    string Content,
    string? ExternalId);

public record UpdateMessageStatusRequest(string Id, MessageStatus Status);

public record MessageDto(
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

public class TicketDraftInput
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Talep";
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public string DueDate { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public int Confidence { get; set; }
    public List<string> Questions { get; set; } = new();
    public List<TicketSubtaskInput> SubTasks { get; set; } = new();
}

public class TicketSubtaskInput
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class TicketAttachmentInput
{
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Data { get; set; } = string.Empty;
}

public class CreateTicketRequest
{
    public string? CustomerId { get; set; }
    public string? PersonId { get; set; }
    public string? DeviceId { get; set; }
    public string? MessageId { get; set; }
    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Ai;
    public bool Publish { get; set; } = true;
    public TicketDraftInput Draft { get; set; } = new();
    public List<TicketAttachmentInput> Attachments { get; set; } = new();
}

public record CreateTicketResponse(string Id, string Status, string? ExternalId = null, string? ExternalUrl = null, string? Error = null);

public record PublishResponse(string Status, string ExternalId, string ExternalUrl);

public record TicketDto(
    string Id,
    string? MessageId,
    string Title,
    string Description,
    string Type,
    TicketPriority Priority,
    string? DueDate,
    List<string> Labels,
    List<TicketSubtask> SubTasks,
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

public record TicketCommentDto(
    string Id,
    string TicketId,
    string? AuthorId,
    string? AuthorName,
    string Body,
    long CreatedAt);

public class UpdateTicketRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public TicketPriority? Priority { get; set; }
    public string? DueDate { get; set; }
    public List<string>? Labels { get; set; }
    public TicketWorkflowStatus? WorkflowStatus { get; set; }
    public string? AssignedTo { get; set; }
}

public class AddTicketCommentRequest
{
    public string Body { get; set; } = string.Empty;
}

public record AnalysisResponse(TicketDraft Ticket, string Mode);
