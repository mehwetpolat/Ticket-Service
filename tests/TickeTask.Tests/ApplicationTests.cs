using TickeTask.Application.Mail;
using TickeTask.Application.Tickets;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Tests;

public sealed class LocalTicketAnalyzerTests
{
    private readonly LocalTicketAnalyzer _analyzer = new();

    [Fact]
    public void Urgent_incident_is_classified_and_dated()
    {
        var draft = _analyzer.Analyze("Bilgisayarım bugün açılmıyor, mavi ekran hatası veriyor. Acil destek gerekiyor.");

        Assert.Equal("Bilgisayarda mavi ekran hatası", draft.Title);
        Assert.Equal("Hata", draft.Type);
        Assert.Equal(TicketPriority.Critical, draft.Priority);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), draft.DueDate);
        Assert.Contains("acil", draft.Labels);
    }

    [Fact]
    public void Request_without_incident_is_normal_priority()
    {
        var draft = _analyzer.Analyze("Yeni bir e-posta imzası hazırlanmasını talep ediyorum.");

        Assert.Equal("Talep", draft.Type);
        Assert.Equal(TicketPriority.Normal, draft.Priority);
        Assert.Equal("Kurum içi BT talebi", draft.Title);
    }

    [Fact]
    public void Explicit_numbered_items_become_subtasks()
    {
        var draft = _analyzer.Analyze("""
            Yeni kullanıcı kurulumu için aşağıdaki işler yapılmalı:
            1. Bilgisayar hazırlanacak
            2. E-posta hesabı açılacak
            3. Lisans atanacak
            """);

        Assert.Equal(3, draft.SubTasks.Count);
        Assert.Equal("Bilgisayar hazırlanacak", draft.SubTasks[0].Title);
    }

    [Fact]
    public void Description_stays_within_ticket_limit()
    {
        var draft = _analyzer.Analyze(new string('a', 5000));

        Assert.True(draft.Description.Length <= 2000);
        Assert.True(draft.Title.Length <= 120);
    }
}

public sealed class MailTriageTests
{
    [Theory]
    [InlineData("Automatic reply: Talebiniz alındı", "Merhaba", true, false, false)]
    [InlineData("Haftalık bülten", "Yeni haberler", false, true, false)]
    [InlineData("Bilgilendirme", "Abonelikten çık", false, false, true)]
    public void Automated_mail_is_skipped(string subject, string body, bool autoSubmitted, bool bulk, bool unsubscribe)
    {
        var result = MailTriage.Triage(new IncomingMail
        {
            Subject = subject,
            Body = body,
            AutoSubmitted = autoSubmitted,
            Bulk = bulk,
            ListUnsubscribe = unsubscribe
        });

        Assert.False(result.Process);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void Greeting_and_signature_only_mail_is_skipped()
    {
        var result = MailTriage.Triage(new IncomingMail
        {
            Subject = "Merhaba",
            Body = "Merhaba,\nİyi çalışmalar.\n\nSaygılarımla,\nMehmet"
        });

        Assert.False(result.Process);
    }

    [Fact]
    public void Concrete_request_is_processed()
    {
        var result = MailTriage.Triage(new IncomingMail
        {
            Subject = "Yazıcı sorunu",
            Body = "Merhaba, ikinci kattaki yazıcıdan belge çıkmıyor. Kontrol edebilir misiniz?\nSaygılarımla"
        });

        Assert.True(result.Process);
    }

    [Fact]
    public void Body_normalization_removes_quotes_urls_and_signatures()
    {
        var body = "Merhaba,\n> eski mesaj\nYazıcı çalışmıyor https://example.com/yardim\n\nSaygılarımla\nMehmet";
        var meaningful = MailTriage.MeaningfulBody(body);

        Assert.Contains("Yazıcı", meaningful);
        Assert.DoesNotContain("eski mesaj", meaningful);
        Assert.DoesNotContain("https", meaningful);
        Assert.DoesNotContain("Mehmet", meaningful);
    }

    [Theory]
    [InlineData("user@example.com", "user", "example.com")]
    [InlineData(" USER@EXAMPLE.COM ", "user", "example.com")]
    public void Split_email_normalizes_address(string value, string local, string domain)
    {
        var result = MailTriage.SplitEmail(value);

        Assert.NotNull(result);
        Assert.Equal((local, domain), result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("user")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user name@example.com")]
    public void Split_email_rejects_invalid_addresses(string value) =>
        Assert.Null(MailTriage.SplitEmail(value));
}

public sealed class TicketTargetCatalogTests
{
    private readonly TicketTargetCatalog _catalog = new();

    [Theory]
    [InlineData("https://github.com/acme/destek/issues", "acme/destek")]
    [InlineData("github.com/acme/destek.git", "acme/destek")]
    [InlineData("acme/destek/", "acme/destek")]
    public void Github_destinations_are_normalized(string input, string expected) =>
        Assert.Equal(expected, _catalog.NormalizeDestination(TicketTarget.Github, input));

    [Fact]
    public void Jira_and_trello_destinations_are_normalized()
    {
        Assert.Equal("HELPDESK", _catalog.NormalizeDestination(TicketTarget.Jira, "helpdesk"));
        Assert.Equal("5f1a2b3c4d5e6f7890abcdef", _catalog.NormalizeDestination(TicketTarget.Trello, "5F1A2B3C4D5E6F7890ABCDEF"));
    }

    [Theory]
    [InlineData(TicketTarget.Jira, "IT", null)]
    [InlineData(TicketTarget.Jira, "1T", "Jira proje anahtarı")]
    [InlineData(TicketTarget.Trello, "5f1a2b3c4d5e6f7890abcdef", null)]
    [InlineData(TicketTarget.Trello, "short", "Trello liste ID")]
    [InlineData(TicketTarget.Github, "acme/destek", null)]
    [InlineData(TicketTarget.Github, "acme", "GitHub deposu")]
    [InlineData(TicketTarget.Asana, "1201234567890123", null)]
    [InlineData(TicketTarget.Asana, "abc", "Asana proje ID")]
    public void Destination_validation_matches_provider_rules(TicketTarget target, string value, string? errorPrefix)
    {
        var error = _catalog.DestinationError(target, value);

        if (errorPrefix is null) Assert.Null(error);
        else Assert.StartsWith(errorPrefix, error);
    }

    [Fact]
    public void Provider_configuration_requires_all_credentials()
    {
        var providers = new ProviderSettings
        {
            Jira = new JiraProviderSettings { BaseUrl = "https://acme.atlassian.net", Email = "it@acme.test", Token = "token" }
        };

        Assert.True(_catalog.ProviderConfigured(TicketTarget.Jira, providers));
        Assert.False(_catalog.ProviderConfigured(TicketTarget.Github, providers));
        Assert.True(_catalog.IsTarget("GITHUB"));
        Assert.Equal("github", _catalog.Get("GitHub").Key);
    }
}

public sealed class TicketDraftFormatterTests
{
    [Fact]
    public void Formatter_includes_details_and_subtasks()
    {
        var text = TicketDraftFormatter.Format(new TicketDraft
        {
            Description = "Yazıcı arızası",
            Type = "Hata",
            Priority = TicketPriority.High,
            DueDate = "2026-10-23",
            Labels = new List<string> { "yazici", "acil" },
            SubTasks = new List<TicketSubtask> { new() { Title = "Kablo kontrolü", Description = "Bağlantıyı doğrula" } }
        });

        Assert.Contains("Yazıcı arızası", text);
        Assert.Contains("Öncelik: Yüksek", text);
        Assert.Contains("Son tarih: 2026-10-23", text);
        Assert.Contains("- Kablo kontrolü: Bağlantıyı doğrula", text);
    }

    [Theory]
    [InlineData("2026-10-22", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("22.10.2026", false)]
    public void Date_validation_is_strict(string value, bool expected) =>
        Assert.Equal(expected, TicketDraftFormatter.IsDateOnly(value));

    [Fact]
    public void Slug_keeps_turkish_letters()
    {
        Assert.Equal("ödeme-hatası", TicketDraftFormatter.Slug("Ödeme Hatası"));
        Assert.Equal("a-b", TicketDraftFormatter.Slug(" a / b "));
    }
}
