using TickeTask.Domain.Models;

namespace TickeTask.Web.Api;

public sealed record AnalyzeRequest(string Message);

public sealed record InboundWebhookRequest(
    string? CustomerName,
    string? Email,
    string? Phone,
    string? Channel,
    string? Sender,
    string? Subject,
    string Content,
    string? ExternalId);

public sealed class MailPollStatusRequest
{
    public string State { get; set; } = "idle";
    public long? CheckedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, MailboxPollStatusRequest>? Mailboxes { get; set; }
}

public sealed class MailboxPollStatusRequest
{
    public string State { get; set; } = "idle";
    public long? CheckedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? LastUid { get; set; }
}

public sealed record ModelStorageRequest(string Action, string? Model, string? Slot, string? Folder, string? Name, string? Path);

public sealed record ModelFolderPickerRequest(string? Path);
