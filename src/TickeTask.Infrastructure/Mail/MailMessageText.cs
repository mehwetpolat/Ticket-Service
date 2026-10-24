using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MimeKit;
using TickeTask.Application.Abstractions;
using TickeTask.Domain.Models;

namespace TickeTask.Infrastructure.Mail;

internal static class MailMessageText
{
    public static string PlainBody(MimeMessage message)
    {
        var text = message.TextBody?.Trim() ?? string.Empty;
        if (text.Length > 0) return Truncate(text, 12_000);
        if (message.HtmlBody is not { Length: > 0 } html) return string.Empty;
        return HtmlToText(html);
    }

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>()) node.Remove();
        var text = HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, "\\n\\s*\\n\\s*\\n+", "\n\n");
        return Truncate(text.Trim(), 12_000);
    }

    public static MailboxAddress? SingleSender(MimeMessage message)
    {
        var mailboxes = message.From.Mailboxes.ToList();
        if (mailboxes.Count != 1) return null;
        var sender = mailboxes[0];
        return sender.Address.Contains('@') && !sender.Address.Any(char.IsWhiteSpace) ? sender : null;
    }

    public static bool AutoSubmitted(MimeMessage message)
    {
        var value = message.Headers["Auto-Submitted"] ?? string.Empty;
        return value.Length > 0 && !value.Contains("no", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Bulk(MimeMessage message) =>
        Regex.IsMatch(message.Headers["Precedence"] ?? string.Empty, "^(bulk|junk|list)$", RegexOptions.IgnoreCase);

    public static string CleanName(string? value) =>
        Regex.Replace((value ?? string.Empty).Replace('\u0000', ' '), "[\\u0000-\\u001f\\u007f]", " ").Trim();

    public static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

internal static class MailStateFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string Path(AppRuntime runtime, MailboxOptions account)
    {
        var key = $"{account.Provider}|{account.Host}|{account.Port}|{account.Secure}|{account.User}|{account.Mailbox}|{account.TenantId}|{account.ClientId}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))
            .ToLowerInvariant()[..24];
        return System.IO.Path.Combine(runtime.MailStateDirectory, $"{hash}.json");
    }

    public static T Read<T>(string path) where T : new()
    {
        try
        {
            var state = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return state ?? new T();
        }
        catch (FileNotFoundException) { return new T(); }
        catch (DirectoryNotFoundException) { return new T(); }
        catch (JsonException) { return new T(); }
        catch (IOException) { return new T(); }
    }

    public static async Task WriteAsync<T>(string path, T state, CancellationToken ct)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = $"{path}.tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, Options), ct);
        File.Move(temporary, path, true);
    }
}
