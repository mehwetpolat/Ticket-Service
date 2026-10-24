using System.Globalization;
using System.Text.RegularExpressions;
using TickeTask.Domain.Models;

namespace TickeTask.Application.Mail;

public record MailTriageResult(bool Process, string Reason);

public static partial class MailTriage
{
    private static readonly string[] AutomatedSubjectPatterns =
    {
        "\\bauto[-\\s]?reply\\b", "\\bautomatic\\s+reply\\b", "\\bauto[-\\s]?response\\b",
        "\\bout\\s+of\\s+(the\\s+)?office\\b", "\\boto?matik\\s+yan[ıi]t\\b", "\\btatilde(yim)?\\b",
        "\\byıllık\\s+izin\\b", "\\bannual\\s+leave\\b", "\\babwesenheit\\b", "\\bundeliverable\\b",
        "\\bdelivery\\s+status\\s+notification\\b", "\\bmail\\s+delivery\\s+(failed|failure)\\b",
        "\\breturned\\s+mail\\b", "\\bdelivery\\s+has\\s+failed\\b", "\\bpostmaster\\b",
        "\\bmailer[-\\s]?daemon\\b", "\\bread\\s+receipt\\b", "\\bokundu\\s+bilgisi\\b",
        "\\bteslim\\s+edilemedi\\b", "\\bgeri\\s+d[öo]nd[üu]\\b", "\\bnewsletter\\b", "\\bb[üu]lten\\b"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "merhaba", "selam", "selamlar", "hi", "hello", "hey", "gunaydin", "günaydın",
        "iyi", "gunler", "günler", "calismalar", "çalışmalar", "dilekler", "dilerim",
        "tesekkur", "teşekkür", "tesekkurler", "teşekkürler", "thanks", "thank", "you",
        "saygilar", "saygılar", "saygilarimla", "saygılarımla", "regards", "best",
        "bilgi", "bilginize", "rica", "ederim", "lütfen", "lutfen", "kolay", "gelsin",
        "bu", "bir", "ve", "ile", "icin", "için", "the", "and", "for", "from", "to",
        "sn", "bay", "bayan", "sayin", "sayın", "degerli", "değerli", "hocam",
        "gonderen", "gönderen", "konu", "subject", "tarih", "date", "email", "e-posta",
        "eposta", "telefon", "phone", "adres", "address", "http", "https", "www",
        "unsubscribe", "abonelikten", "cik", "çık", "cikmak", "çıkmak"
    };

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex("^(--\\s*$|__+\\s*$|sayg[ıi]lar(ımla)?[.,!]?$|te[şs]ekk[üu]rler[.,!]?$|[İi]yi [Ççc]al[ıi][şs]malar[.,!]?$|regards[.,!]?$|best regards[.,!]?$|sent from my |bu e-?posta .*gizli|confidentiality notice|this e-?mail .*confidential)", RegexOptions.IgnoreCase)]
    private static partial Regex SignatureLine();

    public static string NormalizeText(string? value)
    {
        if (value is null) return string.Empty;
        return Regex.Replace(
            Regex.Replace(
                Regex.Replace(value.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00a0', ' '),
                    "[\\u0000-\\u0008\\u000b\\u000c\\u000e-\\u001f\\u007f]", " "),
                "[ \\t]+", " "),
            " *\\n *", "\n").Trim();
    }

    public static string MeaningfulBody(string? body)
    {
        var kept = new List<string>();
        foreach (var line in NormalizeText(body).Split('\n'))
        {
            if (SignatureLine().IsMatch(line)) break;
            if (Regex.IsMatch(line, "^\\s*>")) continue;
            if (Regex.IsMatch(line, "^\\s*(On .+ wrote:|---+\\s*Original Message\\s*---+|G[öo]nderen:|From:)\\s*$", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(line, "^\\s*(On .+|.+ tarihinde .+ yazd[ıi]):?\\s*$", RegexOptions.IgnoreCase)) continue;
            kept.Add(line);
        }
        return Regex.Replace(Regex.Replace(
            Regex.Replace(string.Join("\n", kept), "\\b(?:https?://|www\\.)\\S+", " ", RegexOptions.IgnoreCase),
            @"[^\p{L}\p{N}\s]", " "), "\\s+", " ").Trim();
    }

    public static MailTriageResult Triage(IncomingMail input)
    {
        var subject = NormalizeText(input.Subject);
        var body = NormalizeText(input.Body);
        var attachments = input.AttachmentNames.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        if (input.AutoSubmitted) return new(false, "Otomatik yanıt (Auto-Submitted) olduğu için ticket oluşturulmadı.");
        if (input.Bulk) return new(false, "Toplu/bülten e-postası olduğu için ticket oluşturulmadı.");
        if (input.ListUnsubscribe) return new(false, "Bülten/abonelik e-postası olduğu için ticket oluşturulmadı.");
        if (subject.Length > 0 && AutomatedSubjectPatterns.Any(pattern => Regex.IsMatch(subject, pattern, RegexOptions.IgnoreCase)))
            return new(false, "Otomatik bildirim veya yanıt konusu olduğu için ticket oluşturulmadı.");

        var bodyText = MeaningfulBody(body);
        var words = bodyText.ToLower(Turkish).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => Regex.Replace(word, @"^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$", string.Empty))
            .Where(word => word.Length >= 2 && !StopWords.Contains(word))
            .ToList();

        if (words.Count == 0)
        {
            return new(false, attachments.Count > 0
                ? "E-posta gövdesi boş; yalnızca ek dosya var ve açıklama içermiyor."
                : "E-posta boş veya yalnızca selamlaşma/imza içeriyor.");
        }

        return new(true, string.Empty);
    }

    public static (string Local, string Domain)? SplitEmail(string? email)
    {
        var value = NormalizeText(email).ToLowerInvariant();
        var at = value.LastIndexOf('@');
        if (at < 1 || at == value.Length - 1) return null;
        var local = value[..at];
        var domain = value[(at + 1)..];
        if (local.Length == 0 || domain.Length == 0 || value.Contains(' ')) return null;
        return (local, domain);
    }
}
