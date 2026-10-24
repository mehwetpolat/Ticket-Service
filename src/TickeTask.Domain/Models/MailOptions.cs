namespace TickeTask.Domain.Models;

public enum MailProviderKind
{
    Imap,
    MicrosoftGraph,
    Gmail
}

public class MailboxOptions
{
    public string Id { get; set; } = string.Empty;
    public MailProviderKind Provider { get; set; } = MailProviderKind.Imap;
    public string Label { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public bool Secure { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";

    // Microsoft Graph (client credentials flow / Mail.Read application permission).
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // Google Workspace (OAuth2 refresh token or service account with domain-wide delegation).
    public string RefreshToken { get; set; } = string.Empty;
    public string ServiceAccountJson { get; set; } = string.Empty;
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
    public MailProviderKind Provider { get; set; } = MailProviderKind.Imap;
    public string Label { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool Secure { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Mailbox { get; set; } = "INBOX";
    public bool HasPassword { get; set; }

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool HasClientSecret { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public bool HasRefreshToken { get; set; }
    public string ServiceAccountJson { get; set; } = string.Empty;
    public bool HasServiceAccount { get; set; }
    public bool HasCredentials { get; set; }
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
