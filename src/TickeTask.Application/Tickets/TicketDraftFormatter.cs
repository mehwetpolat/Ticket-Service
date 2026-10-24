using System.Globalization;
using System.Text;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Tickets;

public static class TicketDraftFormatter
{
    private static readonly Dictionary<TicketPriority, string> PriorityLabels = new()
    {
        [TicketPriority.Critical] = "Kritik",
        [TicketPriority.High] = "Yüksek",
        [TicketPriority.Normal] = "Normal"
    };

    public static string Format(TicketDraft draft)
    {
        var lines = new List<string> { (draft.Description ?? string.Empty).Trim() };
        var details = new List<string>
        {
            $"Öncelik: {(PriorityLabels.TryGetValue(draft.Priority, out var label) ? label : draft.Priority.ToString())}",
            $"Tür: {draft.Type}"
        };
        if (!string.IsNullOrWhiteSpace(draft.DueDate)) details.Add($"Son tarih: {draft.DueDate}");
        if (draft.Labels.Count > 0) details.Add($"Etiketler: {string.Join(", ", draft.Labels)}");
        lines.Add(string.Empty);
        lines.Add("--- Ticket bilgileri ---");
        lines.AddRange(details);
        if (draft.SubTasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Yapılacaklar:");
            lines.AddRange(draft.SubTasks.Select(task => $"- {task.Title}{(string.IsNullOrWhiteSpace(task.Description) ? string.Empty : $": {task.Description}")}"));
        }
        return string.Join("\n", lines).Trim();
    }

    public static bool IsDateOnly(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static long UnixAt(string date, int hour)
    {
        var parsed = DateTime.SpecifyKind(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);
        return new DateTimeOffset(parsed.AddHours(hour)).ToUnixTimeMilliseconds();
    }

    public static string Slug(string label)
    {
        var builder = new StringBuilder();
        foreach (var character in label.ToLower(CultureInfo.GetCultureInfo("tr-TR")))
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_') builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }
}
