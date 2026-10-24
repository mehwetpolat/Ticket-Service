namespace TickeTask.Domain.Models;

public enum MailCheckState
{
    Idle,
    Connected,
    Error
}

public class MailboxPollStatus
{
    public MailCheckState State { get; set; } = MailCheckState.Idle;
    public long CheckedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? LastUid { get; set; }
}

public class MailPollStatus
{
    public MailCheckState State { get; set; } = MailCheckState.Idle;
    public long CheckedAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, MailboxPollStatus>? Mailboxes { get; set; }
}
