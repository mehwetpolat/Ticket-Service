namespace TickeTask.Domain.Entities;

public class Session
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
    public long CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
