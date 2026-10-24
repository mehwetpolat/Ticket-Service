namespace TickeTask.Infrastructure.Mail;

internal static class GmailLabels
{
    private static readonly HashSet<string> SystemLabelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "INBOX", "SPAM", "TRASH", "UNREAD", "STARRED", "IMPORTANT", "SENT", "DRAFT", "CHAT",
        "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES",
        "CATEGORY_FORUMS", "CATEGORY_RESERVATIONS", "CATEGORY_PURCHASES"
    };

    public static bool TryNormalizeSystemLabelId(string value, out string normalized)
    {
        if (SystemLabelIds.Contains(value))
        {
            normalized = value.ToUpperInvariant();
            return true;
        }
        normalized = string.Empty;
        return false;
    }

    public static bool IsCustomLabelId(string value) =>
        value.StartsWith("Label_", StringComparison.OrdinalIgnoreCase);

    public static string? FindByName(IEnumerable<(string Id, string Name)> labels, string name)
    {
        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label.Id) &&
                string.Equals(label.Name, name, StringComparison.OrdinalIgnoreCase))
                return label.Id;
        }
        return null;
    }
}
