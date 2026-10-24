namespace TickeTask.Domain.Models;

public class MailboxOptions
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool Secure { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";
}

public class MailOptions
{
    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public bool ImportExisting { get; set; }
    public int MaxPerPoll { get; set; } = 25;
    public List<MailboxOptions> Mailboxes { get; set; } = new();
}

public class AdminMailbox
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool Secure { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";
    public bool HasPassword { get; set; }
}

public class AdminMailOptions
{
    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public bool ImportExisting { get; set; }
    public int MaxPerPoll { get; set; } = 25;
    public List<AdminMailbox> Mailboxes { get; set; } = new();
}
