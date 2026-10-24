namespace TickeTask.Domain.Entities;

public class AppSetting
{
    public string OwnerId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}
