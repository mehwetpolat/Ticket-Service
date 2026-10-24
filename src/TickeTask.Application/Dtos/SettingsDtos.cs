using TickeTask.Domain.Models;

namespace TickeTask.Application.Dtos;

public record ProviderSaveRequest(string Provider, Dictionary<string, string> Values);

public record SaveLlmRequest(string Endpoint, string Model);

public record ProviderStateDto(
    string? BaseUrl,
    string? Email,
    bool HasToken,
    bool? HasKey,
    bool? HasBaseUrl);

public record SettingsResponse(LlmSettings Llm, Dictionary<string, ProviderStateDto> Providers);

public record TestProviderRequest(string Provider);

public record MailSenderDto(string? Id, string Email, string Name, bool Active, bool FromEnv);

public record MailDomainDto(string Domain, bool FromEnv);

public record SaveMailSenderRequest(string Email, string Name, bool Active = true);

public record SaveMailDomainRequest(string Domain);

public record MailIngestionsResponse(List<MailIngestionDto> Items, Dictionary<string, int> Stats);

public record MailIngestionDto(
    string Id,
    string Mailbox,
    string MessageId,
    string SenderEmail,
    string SenderName,
    string Subject,
    string Status,
    int Attempts,
    string? Error,
    string? TicketId,
    long CreatedAt,
    long UpdatedAt,
    string? ExternalId,
    string? ExternalUrl,
    string? Target);
