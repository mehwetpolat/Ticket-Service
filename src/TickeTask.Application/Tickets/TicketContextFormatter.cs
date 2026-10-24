namespace TickeTask.Application.Tickets;

public static class TicketContextFormatter
{
    public static string AppendContext(string description, IEnumerable<string> lines)
    {
        var context = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToList();
        var body = description.Trim();
        if (context.Count == 0) return body;
        return $"{body}\n\nBağlam\n{string.Join("\n", context)}";
    }

    public static string AppendSource(
        string description,
        string senderName,
        string senderEmail,
        string? subject,
        string? messageId,
        IReadOnlyCollection<string> attachments,
        string body)
    {
        var lines = new List<string>
        {
            "Kaynak mesaj",
            $"Gönderen: {senderName} <{senderEmail}>",
            $"Konu: {Fallback(subject, "(Konu yok)")}"
        };
        if (!string.IsNullOrWhiteSpace(messageId)) lines.Add($"Message-ID: {messageId}");
        if (attachments.Count > 0) lines.Add($"Ek dosyalar: {string.Join(", ", attachments)}");
        lines.Add(string.Empty);
        lines.Add(Fallback(body, "(Mesaj gövdesi boş)"));
        return $"{description.Trim()}\n\n{string.Join("\n", lines)}";
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
