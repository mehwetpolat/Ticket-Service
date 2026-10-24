namespace TickeTask.Domain.Entities;

public class Device
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "other";
    public string? IpAddress { get; set; }
    public string? Serial { get; set; }
    public string? Location { get; set; }
    public string? PersonId { get; set; }
    public string? ParentId { get; set; }
    public string? Notes { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public Person? Person { get; set; }
}
