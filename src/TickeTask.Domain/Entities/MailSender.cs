namespace TickeTask.Domain.Entities;

public class MailSender
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
