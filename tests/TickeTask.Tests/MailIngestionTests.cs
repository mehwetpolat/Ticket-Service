using Microsoft.EntityFrameworkCore;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Domain.Models;

namespace TickeTask.Tests;

public sealed class MailIngestionTests
{
    [Fact]
    public async Task Dry_run_creates_message_and_ticket_without_publishing()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync();

        var result = await harness.Ingestion.AcceptAsync(Mail("msg-1"));

        Assert.Equal("dry_run", result.Status);
        Assert.NotNull(result.TicketId);
        Assert.Empty(harness.Publisher.Calls);

        var ticket = await harness.Tickets.GetAsync(result.TicketId!, "local-user");
        Assert.NotNull(ticket);
        Assert.Equal(TicketStatus.Draft, ticket!.Status);
        Assert.Equal(TicketTarget.Jira, ticket.Target);
        Assert.Equal("IT", ticket.DestinationId);
        Assert.Contains("Kaynak mesaj", ticket.Description);

        var message = await harness.Database.Db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.New, message.Status);
        Assert.Contains("yazıcıdan belge çıkmıyor", message.Content);

        var ingestion = await harness.Database.Db.MailIngestions.SingleAsync();
        Assert.Equal(MailIngestionStatus.DryRun, ingestion.Status);
        Assert.Equal("msg-1", ingestion.MessageId);
    }

    [Fact]
    public async Task Duplicate_message_id_is_not_processed_twice()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync();

        var first = await harness.Ingestion.AcceptAsync(Mail("same-id"));
        var second = await harness.Ingestion.AcceptAsync(Mail("same-id"));

        Assert.Equal("dry_run", first.Status);
        Assert.Equal(first.TicketId, second.TicketId);
        Assert.Equal(1, await harness.Database.Db.MailIngestions.CountAsync());
        Assert.Equal(1, await harness.Database.Db.Tickets.CountAsync());
        Assert.Equal(1, await harness.Database.Db.Messages.CountAsync());
    }

    [Fact]
    public async Task Disallowed_sender_is_ignored_before_persistence()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync();
        var mail = Mail("denied");
        mail.SenderEmail = "stranger@other.test";

        var result = await harness.Ingestion.AcceptAsync(mail);

        Assert.Equal("ignored", result.Status);
        Assert.Equal(0, await harness.Database.Db.MailIngestions.CountAsync());
    }

    [Fact]
    public async Task Automated_mail_is_recorded_as_ignored()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync();
        var mail = Mail("auto");
        mail.AutoSubmitted = true;

        var result = await harness.Ingestion.AcceptAsync(mail);

        Assert.Equal("ignored", result.Status);
        Assert.Equal(0, await harness.Database.Db.Tickets.CountAsync());
        var ingestion = await harness.Database.Db.MailIngestions.SingleAsync();
        Assert.Equal(MailIngestionStatus.Ignored, ingestion.Status);
        Assert.False(string.IsNullOrWhiteSpace(ingestion.Error));
    }

    [Fact]
    public async Task Enabled_live_mode_publishes_ticket_and_marks_message_converted()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync(dryRun: false);

        var result = await harness.Ingestion.AcceptAsync(Mail("live"));

        Assert.Equal("published", result.Status);
        Assert.Equal("EXT-1", result.ExternalId);
        Assert.Single(harness.Publisher.Calls);
        var ticket = await harness.Tickets.GetAsync(result.TicketId!, "local-user");
        Assert.Equal(TicketStatus.Published, ticket!.Status);
        Assert.Equal("EXT-1", ticket.ExternalId);
        var message = await harness.Database.Db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Converted, message.Status);
        var ingestion = await harness.Database.Db.MailIngestions.SingleAsync();
        Assert.Equal(MailIngestionStatus.Published, ingestion.Status);
    }

    [Fact]
    public async Task Publish_failure_is_retryable_and_then_succeeds()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync(dryRun: false);
        harness.Publisher.Failure = new InvalidOperationException("Jira erişilemiyor");

        var failed = await harness.Ingestion.AcceptAsync(Mail("retry"));

        Assert.Equal("failed", failed.Status);
        Assert.Contains("Jira erişilemiyor", failed.Error);
        var ticket = await harness.Tickets.GetAsync(failed.TicketId!, "local-user");
        Assert.Equal(TicketStatus.Failed, ticket!.Status);
        var ingestion = await harness.Database.Db.MailIngestions.SingleAsync();
        Assert.Equal(MailIngestionStatus.Failed, ingestion.Status);

        harness.Publisher.Failure = null;
        harness.Clock.Advance(61);
        var retried = await harness.Ingestion.RetryFailedAsync();

        Assert.Single(retried);
        var retriedTicket = await harness.Tickets.GetAsync(failed.TicketId!, "local-user");
        Assert.Equal(TicketStatus.Published, retriedTicket!.Status);
    }

    [Fact]
    public async Task Disabled_mail_configuration_fails_processing()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync();
        var config = await harness.MailConfig.GetAsync();
        Assert.NotNull(config);
        config!.Enabled = false;
        await harness.MailConfig.SaveAsync(config);

        var result = await harness.Ingestion.AcceptAsync(Mail("disabled"));

        Assert.Equal("failed", result.Status);
        Assert.Contains("kapatıldı", result.Error);
    }

    [Fact]
    public async Task Sender_inventory_match_is_added_to_ticket_and_published_draft()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.ConfigureAsync(dryRun: false);
        var now = harness.Clock.UnixNow;
        harness.Database.Db.People.Add(new Person
        {
            Id = "prs-match",
            Name = "Mehmet Kullanıcı",
            Email = "user@example.com",
            Department = "Satış",
            CreatedAt = now,
            UpdatedAt = now
        });
        harness.Database.Db.Devices.Add(new Device
        {
            Id = "dev-match",
            Name = "İkinci Kat Yazıcı",
            Kind = "printer",
            IpAddress = "10.0.0.42",
            Serial = "SN-42",
            Location = "İkinci kat",
            PersonId = "prs-match",
            CreatedAt = now,
            UpdatedAt = now
        });
        await harness.Database.Db.SaveChangesAsync();

        var result = await harness.Ingestion.AcceptAsync(Mail("identity"));

        Assert.Equal("published", result.Status);
        var ticket = await harness.Tickets.GetAsync(result.TicketId!, "local-user");
        Assert.Equal("prs-match", ticket!.PersonId);
        Assert.Equal("dev-match", ticket.DeviceId);
        Assert.Contains("Etkilenen çalışan: Mehmet Kullanıcı / Satış / user@example.com", ticket.Description);
        Assert.Contains("Cihaz: İkinci Kat Yazıcı (printer)", ticket.Description);
        Assert.Contains("İç IP: 10.0.0.42", ticket.Description);
        Assert.Contains("Kaynak mesaj", ticket.Description);
        var published = Assert.Single(harness.Publisher.Calls);
        Assert.Contains("Etkilenen çalışan: Mehmet Kullanıcı", published.Draft.Description);
        Assert.Contains("Cihaz: İkinci Kat Yazıcı", published.Draft.Description);
    }

    private static IncomingMail Mail(string messageId) => new()
    {
        Mailbox = "IT",
        MessageId = messageId,
        SenderEmail = "user@example.com",
        SenderName = "Mehmet Kullanıcı",
        Subject = "Yazıcı çalışmıyor",
        Body = "Merhaba, ikinci kattaki yazıcıdan belge çıkmıyor. Acil destek gerekiyor.\nSaygılarımla",
        AttachmentNames = new List<string> { "ekran.png" }
    };
}
