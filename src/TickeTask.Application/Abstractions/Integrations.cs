using TickeTask.Application.Dtos;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Abstractions;

public interface ITicketTargetCatalog
{
    IReadOnlyList<TicketTarget> Targets { get; }
    bool IsTarget(string value);
    TargetDefinition Get(TicketTarget target);
    TargetDefinition Get(string target);
    string NormalizeDestination(TicketTarget target, string value);
    string? DestinationError(TicketTarget target, string value);
    bool ProviderConfigured(TicketTarget target, ProviderSettings providers);
}

public record TargetDefinition(
    TicketTarget Target,
    string Key,
    string Label,
    string ShortLabel,
    string PublishLabel,
    string DestinationLabel,
    string DestinationPlaceholder,
    string DestinationHelp,
    string DocsUrl,
    string ApiDocsUrl,
    string PatternError,
    bool Attachments);

public interface ITicketPublisher
{
    Task<PublishResult> PublishAsync(string ownerId, string ticketId, TicketTarget target, string destinationId, TicketDraft draft, CancellationToken ct = default);
}

public record PublishResult(string ExternalId, string ExternalUrl);

public interface ITicketProviderProbe
{
    Task<ProbeResult> ProbeAsync(string ownerId, TicketTarget target, string destinationId, CancellationToken ct = default);
}

public record ProbeResult(string Account, bool DestinationChecked, string DestinationName);

public interface ISetupStatusService
{
    Task<SetupStatusDto> GetAsync(string requestOrigin, CancellationToken ct = default);
}

public interface IModelControlClient
{
    Task<ModelStorageStatus?> GetStatusAsync(CancellationToken ct = default);
    Task<ModelFolderListing?> GetFoldersAsync(string slot, string folder, CancellationToken ct = default);
    Task<ModelFolderListing?> CreateFolderAsync(string slot, string folder, string name, CancellationToken ct = default);
    Task<ModelStorageSelection?> SelectAsync(string slot, string folder, CancellationToken ct = default);
    Task<ModelStorageSelection?> SelectPathAsync(string path, CancellationToken ct = default);
    Task PullAsync(string model, CancellationToken ct = default);
    Task CancelAsync(CancellationToken ct = default);
}

public interface IModelFolderPicker
{
    Task<string?> PickAsync(string? initialPath, CancellationToken ct = default);
}

public interface IMessageIngestionService
{
    Task<(string Id, bool Duplicate)> IngestAsync(
        string ownerId,
        string? customerName,
        string? email,
        string? phone,
        MessageChannel channel,
        string? sender,
        string? subject,
        string content,
        string? externalId,
        CancellationToken ct = default);
}

public interface ISessionAuthService
{
    Task<SessionUserDto> GetActorAsync(CancellationToken ct = default);
    Task<string> GetOwnerIdAsync(CancellationToken ct = default);
    Task<SessionUserDto> RequireAdminAsync(CancellationToken ct = default);
}
