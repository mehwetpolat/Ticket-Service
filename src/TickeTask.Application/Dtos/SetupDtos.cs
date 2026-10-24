using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Dtos;

public record SetupStepDto(string Id, string Title, string Description, bool Complete, string Detail, string Section);

public record SetupCountsDto(int DryRun, int Published, int Failed, int Ignored, int TicketsPublished);

public record SetupMailDto(bool Configured, bool Enabled, bool DryRun, int MailboxCount, MailPollStatus? Status);

public record SetupTicketSystemDto(
    TicketTarget Target,
    string Label,
    string ShortLabel,
    string DestinationLabel,
    string DestinationHelp,
    string DocsUrl,
    string ApiDocsUrl,
    bool Configured,
    bool Tested,
    string Project,
    bool ProjectChecked);

public record SetupLlmDto(bool Connected, bool Installed, string Model, string Endpoint);

public record SetupSendersDto(int Addresses, int Domains);

public record SetupStatusDto(
    List<SetupStepDto> Steps,
    bool Complete,
    string? NextStep,
    SetupCountsDto Counts,
    SetupMailDto Mail,
    SetupTicketSystemDto TicketSystem,
    SetupLlmDto Llm,
    SetupSendersDto Senders,
    string AppUrl,
    long ServerTime);

public record ConfigDto(
    bool LocalModel,
    bool Jira,
    bool Trello,
    bool Github,
    bool Asana,
    bool Clickup,
    bool InboundWebhook,
    bool Whatsapp,
    bool LocalMode);

public record HealthDto(string Status, long Time, HealthChecksDto Checks);

public record HealthChecksDto(bool Database, bool Encryption);
