using TickeTask.Domain.Enums;

namespace TickeTask.Application.Dtos;

public record SaveProjectRequest(TicketTarget Target, string DestinationId, string Name);

public record SaveCustomerRequest(string? Id, string Name, string? Email, string? Phone, TicketTarget Target, string DestinationId);

public record CustomerDto(
    string Id,
    string Name,
    string? Email,
    string? Phone,
    TicketTarget Target,
    string DestinationId,
    long CreatedAt);

public record SavePersonRequest(string? Id, string Name, string? Email, string? Department, string? Phone, string? UserId);

public record PersonDto(string Id, string Name, string? Email, string? Department, string? Phone, string? UserId);

public record SaveDeviceRequest(
    string? Id,
    string Name,
    string Kind,
    string? IpAddress,
    string? Serial,
    string? Location,
    string? PersonId,
    string? ParentId,
    string? Notes);

public record DeviceDto(
    string Id,
    string Name,
    string Kind,
    string? IpAddress,
    string? Serial,
    string? Location,
    string? PersonId,
    string? PersonName,
    string? ParentId,
    string? Notes);
