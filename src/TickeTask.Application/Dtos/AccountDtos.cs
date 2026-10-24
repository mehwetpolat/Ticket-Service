using TickeTask.Domain.Enums;

namespace TickeTask.Application.Dtos;

public record SessionUserDto(string Id, string Name, string Email, UserRole Role);

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Name, string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UpdateUserRequest(string Id, UserStatus Status);

public record UserRecordDto(
    string Id,
    string Name,
    string Email,
    UserRole Role,
    UserStatus Status,
    long CreatedAt,
    long? ApprovedAt,
    string? PersonId);
