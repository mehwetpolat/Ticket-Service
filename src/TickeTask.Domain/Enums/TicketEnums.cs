namespace TickeTask.Domain.Enums;

public enum TicketTarget
{
    Jira,
    Trello,
    Github,
    Asana,
    Clickup
}

public enum TicketPriority
{
    Critical,
    High,
    Normal
}

public enum TicketStatus
{
    Draft,
    Published,
    Failed
}

public enum TicketWorkflowStatus
{
    Open,
    InProgress,
    Waiting,
    Resolved,
    Closed
}

public enum AnalysisMode
{
    Ai,
    Local
}

public enum MessageChannel
{
    Manual,
    Email,
    Whatsapp,
    Webhook
}

public enum MessageStatus
{
    New,
    Analyzed,
    Converted,
    Ignored
}

public enum MailIngestionStatus
{
    Pending,
    Processing,
    Failed,
    Published,
    DryRun,
    Ignored
}

public enum UserRole
{
    Admin,
    Member
}

public enum UserStatus
{
    Pending,
    Active,
    Disabled
}

public enum IntegrationLogLevel
{
    Info,
    Error
}
