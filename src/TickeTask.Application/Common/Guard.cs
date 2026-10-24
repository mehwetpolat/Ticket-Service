namespace TickeTask.Application.Common;

public static class Guard
{
    public static void Required(bool condition, string message)
    {
        if (!condition) throw ApiException.BadRequest(message);
    }

    public static string Text(string? value, int min, int max, string message)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length < min || trimmed.Length > max) throw ApiException.BadRequest(message);
        return trimmed;
    }

    public static string? OptionalText(string? value, int max, string message)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length > max) throw ApiException.BadRequest(message);
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static void MaxLength(string? value, int max, string message)
    {
        if ((value ?? string.Empty).Length > max) throw ApiException.BadRequest(message);
    }
}
