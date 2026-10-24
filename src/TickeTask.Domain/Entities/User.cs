using TickeTask.Domain.Enums;

namespace TickeTask.Domain.Entities;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public long CreatedAt { get; set; }
    public long? ApprovedAt { get; set; }

    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
