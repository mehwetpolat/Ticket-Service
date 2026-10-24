using System.Globalization;
using System.Text.RegularExpressions;
using TickeTask.Application.Abstractions;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Tickets;

public class LocalTicketAnalyzer : ILocalAnalyzer
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public TicketDraft Analyze(string message)
    {
        var cleaned = CleanMessage(message);
        var text = cleaned.ToLower(Turkish);
        var isIncident = Contains(text, "çöktü", "çalışmıyor", "hata", "ulaşmıyor", "bozuk", "problem", "açılmıyor", "mavi ekran", "kesildi");
        var isUrgent = Contains(text, "acil", "hemen", "bugün", "satış kaçır", "kritik");
        return new TicketDraft
        {
            Title = TitleFor(text, isIncident, isUrgent),
            Description = Summarize(cleaned, isIncident, isUrgent),
            Type = isIncident ? "Hata" : "Talep",
            Priority = isUrgent ? TicketPriority.Critical : isIncident ? TicketPriority.High : TicketPriority.Normal,
            DueDate = Contains(text, "bugün") ? IsoDate(0) : Contains(text, "yarın") ? IsoDate(1) : string.Empty,
            Labels = isIncident
                ? new List<string> { "bt-destek", "hata" }.Concat(isUrgent ? new[] { "acil" } : Array.Empty<string>()).ToList()
                : new List<string> { "bt-talebi" },
            Confidence = 72,
            Questions = isIncident && text.Contains("hata kodu", StringComparison.Ordinal)
                ? new List<string> { "Ekrandaki hata kodu nedir?", "Sorun hangi işletim sistemi ve cihazda oluşuyor?" }
                : new List<string>(),
            SubTasks = ExtractExplicitTasks(message)
        };
    }

    private static bool Contains(string text, params string[] terms) => terms.Any(text.Contains);

    private static string CleanMessage(string message) => Regex.Replace(
        Regex.Replace(message.Replace('\u00a0', ' '),
            "\\b(iyi çalışmalar|iyi calismalar|saygılarımla|saygilarimla|teşekkürler|tesekkurler)\\b[.!]?",
            string.Empty, RegexOptions.IgnoreCase),
        "\\s+", " ").Trim();

    private static string Summarize(string text, bool isIncident, bool isUrgent)
    {
        var parts = new List<string>();
        var noApps = Regex.IsMatch(text, "hiçbir şey açılmıyor|hicbir sey acilmiyor|hiçbir uygulama açılamıyor|hicbir uygulama acilamiyor", RegexOptions.IgnoreCase);
        var systemDown = Regex.IsMatch(text, "sistem çöktü|sistem çökmüş|sistem kullanılamıyor|sistem kullanilamiyor|çalışmıyor|calismiyor", RegexOptions.IgnoreCase);
        var errorOnScreen = Regex.IsMatch(text, "hata kodu|hata mesajı|hata mesaji", RegexOptions.IgnoreCase);
        var errorStays = Regex.IsMatch(text, "gitmedi|kaybolmadı|kaybolmadi|kapanmadı|kapanmadi|tamam dedim", RegexOptions.IgnoreCase);
        var blueScreen = Regex.IsMatch(text, "mavi ekran|blue screen|bsod", RegexOptions.IgnoreCase);
        var noAccess = Regex.IsMatch(text, "ulaşamıyorum|ulasamiyorum|erişemiyorum|erisemiyorum|erişim sağlayamıyorum|erisim saglayamiyorum", RegexOptions.IgnoreCase);

        if (blueScreen) parts.Add("Bilgisayarda mavi ekran hatası oluşuyor");
        if (noAccess) parts.Add("bilgisayara erişilemiyor");
        if (systemDown || noApps) parts.Add("Sistem kullanılamıyor");
        if (noApps) parts.Add("hiçbir uygulama açılamıyor");
        if (errorOnScreen && errorStays) parts.Add("hata kodu ekranda kalıyor");
        else if (errorOnScreen) parts.Add("ekranda hata kodu görüntüleniyor");
        if (parts.Count == 0) parts.Add(isIncident ? "Kurum içi BT sorunu bildirildi" : "Kurum içi BT talebi iletildi");

        var summary = $"{parts[0]}{(parts.Count > 1 ? $", {string.Join(" ve ", parts.Skip(1))}" : string.Empty)}.";
        var urgency = isUrgent ? " Acil destek gerekiyor." : " BT ekibinin incelemesi gerekiyor.";
        var result = summary + urgency;
        if (result.Length > 0) result = char.ToUpper(result[0], Turkish) + result[1..];
        return result.Length > 2000 ? result[..2000] : result;
    }

    private static string TitleFor(string text, bool isIncident, bool isUrgent)
    {
        if (isIncident && text.Contains("hata kodu", StringComparison.Ordinal) && Regex.IsMatch(text, "hiçbir şey açılmıyor|hicbir sey acilmiyor|uygulama", RegexOptions.IgnoreCase))
            return isUrgent ? "Bilgisayar ve uygulamalar açılmıyor — acil destek" : "Bilgisayar ve uygulamalar açılmıyor";
        if (isIncident && Regex.IsMatch(text, "ödeme|odeme", RegexOptions.IgnoreCase)) return "Ödeme işleminde hata oluşuyor";
        if (Regex.IsMatch(text, "mavi ekran|blue screen|bsod", RegexOptions.IgnoreCase)) return "Bilgisayarda mavi ekran hatası";
        if (text.Contains("powerpoint", StringComparison.Ordinal) && Regex.IsMatch(text, "lisans|license", RegexOptions.IgnoreCase)) return "PowerPoint lisans sorunu";
        if (text.Contains("powerpoint", StringComparison.Ordinal)) return "PowerPoint açılmıyor";
        if (Regex.IsMatch(text, "lisans|license", RegexOptions.IgnoreCase)) return "Yazılım lisansı desteği";
        if (Regex.IsMatch(text, "yazıcı|yazici|printer", RegexOptions.IgnoreCase)) return "Yazıcı sorunu";
        if (Regex.IsMatch(text, "internet|ağ bağlantı|wifi|wi-fi", RegexOptions.IgnoreCase)) return "Ağ bağlantısı sorunu";
        if (isIncident) return isUrgent ? "BT sorunu için acil destek" : "BT sorunu incelenmeli";
        return "Kurum içi BT talebi";
    }

    private static List<TicketSubtask> ExtractExplicitTasks(string message)
    {
        var items = message.Split('\n')
            .Select(line => Regex.Match(line, "^\\s*(?:[-*•]|\\d+[.)])\\s+(.+)$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(item => item.Length > 0)
            .ToList();
        return items.Count >= 2
            ? items.Take(8).Select(title => new TicketSubtask { Title = title.Length > 120 ? title[..120] : title }).ToList()
            : new List<TicketSubtask>();
    }

    private static string IsoDate(int addDays) => DateTime.UtcNow.AddDays(addDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
