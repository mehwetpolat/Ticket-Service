using System.Text.Json;
using TickeTask.Application.Common;
using TickeTask.Infrastructure.Mail;

namespace TickeTask.Tests;

public sealed class MailProviderHardeningTests
{
    [Fact]
    public void Poll_schedule_honors_interval_and_config_changes()
    {
        var schedule = new MailPollSchedule();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(schedule.ShouldPoll(start, "config-a"));
        schedule.MarkPolled(start, TimeSpan.FromSeconds(60));
        Assert.False(schedule.ShouldPoll(start.AddSeconds(59), "config-a"));
        Assert.True(schedule.ShouldPoll(start.AddSeconds(60), "config-a"));

        schedule.MarkPolled(start.AddSeconds(60), TimeSpan.FromSeconds(60));
        Assert.False(schedule.ShouldPoll(start.AddSeconds(61), "config-a"));
        Assert.True(schedule.ShouldPoll(start.AddSeconds(61), "config-b"));

        schedule.Reset();
        Assert.True(schedule.ShouldPoll(start, "config-c"));
    }

    [Fact]
    public void Graph_well_known_folders_are_normalized()
    {
        Assert.Equal("inbox", GraphMailProvider.NormalizeFolder("INBOX"));
        Assert.Equal("inbox", GraphMailProvider.NormalizeFolder(" "));
        Assert.Equal("sentitems", GraphMailProvider.NormalizeFolder("SentItems"));
        Assert.Equal("syncissues", GraphMailProvider.NormalizeFolder("SyncIssues"));
        Assert.Equal("AAMkADExample=", GraphMailProvider.NormalizeFolder("AAMkADExample="));
        Assert.Equal("Gelen Kutusu", GraphMailProvider.NormalizeFolder("Gelen Kutusu"));
    }

    [Fact]
    public void Gmail_label_names_are_resolved_and_system_ids_are_normalized()
    {
        Assert.True(GmailLabels.TryNormalizeSystemLabelId("inbox", out var normalized));
        Assert.Equal("INBOX", normalized);
        Assert.True(GmailLabels.TryNormalizeSystemLabelId("category_personal", out normalized));
        Assert.Equal("CATEGORY_PERSONAL", normalized);
        Assert.False(GmailLabels.TryNormalizeSystemLabelId("Destek", out _));
        Assert.True(GmailLabels.IsCustomLabelId("Label_123"));
        Assert.False(GmailLabels.IsCustomLabelId("INBOX"));

        var labels = new[] { ("Label_123", "Destek"), ("Label_456", "Diğer") };
        Assert.Equal("Label_123", GmailLabels.FindByName(labels, "destek"));
        Assert.Null(GmailLabels.FindByName(labels, "Yok"));
    }

    [Fact]
    public void Gmail_internal_date_accepts_string_and_number_milliseconds()
    {
        Assert.Equal(1_700_000_000, ReadInternalDate("""{"internalDate":"1700000000123"}"""));
        Assert.Equal(1_700_000_000, ReadInternalDate("""{"internalDate":1700000000123}"""));
        Assert.Equal(1_700_000_000, ReadInternalDate("""{"internalDate":"1700000000"}"""));
        Assert.Equal(0, ReadInternalDate("""{"internalDate":0}"""));
        Assert.Equal(0, ReadInternalDate("{}"));
    }

    [Fact]
    public void Service_account_parser_rejects_malformed_shapes_and_keys()
    {
        var account = GoogleServiceAccountJson.Parse("""{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n"}""");
        Assert.Equal("mail@project.iam.gserviceaccount.com", account.ClientEmail);
        Assert.Null(account.TokenUri);

        var arrayError = Assert.Throws<ApiException>(() => GoogleServiceAccountJson.Parse("[]"));
        Assert.Contains("geçerli değil", arrayError.Message);

        var keyError = Assert.Throws<ApiException>(() => GoogleServiceAccountJson.Parse("""{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"not-a-pem"}"""));
        Assert.Contains("PEM", keyError.Message);

        var tokenError = Assert.Throws<ApiException>(() => GoogleServiceAccountJson.Parse("""{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n","token_uri":"http://example.com/token"}"""));
        Assert.Contains("HTTPS", tokenError.Message);
    }

    private static long ReadInternalDate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GmailMailProvider.ReadInternalDate(document.RootElement);
    }
}
