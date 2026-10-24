namespace TickeTask.Domain.Entities;

public class Person
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Phone { get; set; }
    public string? UserId { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public User? User { get; set; }
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
