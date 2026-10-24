using Microsoft.EntityFrameworkCore;
using TickeTask.Domain.Entities;
using TickeTask.Domain.Enums;
using TickeTask.Infrastructure.Persistence.Repositories;
using TickeTask.Infrastructure.Security;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Tests;

public sealed class SettingRepositoryTests
{
    [Fact]
    public async Task Settings_are_encrypted_at_rest_and_round_trip()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = new SettingRepository(database.Db, new EncryptionService(database.Runtime));

        await repository.SetAsync("local-user", "providers", new { token = "super-secret" });

        var raw = await database.Db.AppSettings.SingleAsync();
        Assert.DoesNotContain("super-secret", raw.Value);
        Assert.DoesNotContain("token", raw.Value);

        var value = await repository.GetAsync<Dictionary<string, string>>("local-user", "providers");
        Assert.NotNull(value);
        Assert.Equal("super-secret", value!["token"]);
    }

    [Fact]
    public async Task Missing_setting_returns_default()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = new SettingRepository(database.Db, new EncryptionService(database.Runtime));

        Assert.Null(await repository.GetAsync<object>("local-user", "missing"));
    }
}

public sealed class CustomerRepositoryTests
{
    [Fact]
    public async Task Customer_related_counts_include_messages_and_tickets()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = new CustomerRepository(database.Db);
        var customer = NewCustomer("Acme");
        await repository.AddAsync(customer);

        await database.Db.Messages.AddAsync(NewMessage(customer.Id));
        await database.Db.Tickets.AddAsync(NewTicket(customer.Id));
        await database.Db.SaveChangesAsync();

        Assert.Equal(2, await repository.CountRelatedAsync(customer.Id, "local-user"));
        Assert.Equal(0, await repository.CountRelatedAsync(customer.Id, "other-owner"));
    }

    [Fact]
    public async Task Duplicate_customer_name_is_found_case_insensitively()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = new CustomerRepository(database.Db);
        await repository.AddAsync(NewCustomer("Acme Destek"));

        var duplicate = await repository.FindByNameAsync("local-user", "acme destek");

        Assert.NotNull(duplicate);
        Assert.Equal("Acme Destek", duplicate!.Name);
        Assert.Null(await repository.FindByNameAsync("other-owner", "Acme Destek"));
    }

    [Fact]
    public async Task Ingest_lookup_matches_email_phone_or_name()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = new CustomerRepository(database.Db);
        await repository.AddAsync(NewCustomer("Acme", email: "destek@acme.test", phone: "555"));

        Assert.NotNull(await repository.FindForIngestAsync("local-user", "destek@acme.test", "", ""));
        Assert.NotNull(await repository.FindForIngestAsync("local-user", "", "555", ""));
        Assert.NotNull(await repository.FindForIngestAsync("local-user", "", "", "Acme"));
        Assert.Null(await repository.FindForIngestAsync("local-user", "", "", "Başka"));
    }

    private static Customer NewCustomer(string name, string? email = null, string? phone = null) => new()
    {
        Id = $"cus_{Guid.NewGuid():N}",
        OwnerId = "local-user",
        Name = name,
        Email = email,
        Phone = phone,
        Target = TicketTarget.Jira,
        DestinationId = "IT",
        CreatedAt = 1_700_000_000,
        UpdatedAt = 1_700_000_000
    };

    private static Message NewMessage(string customerId) => new()
    {
        Id = $"msg_{Guid.NewGuid():N}",
        OwnerId = "local-user",
        CustomerId = customerId,
        Channel = MessageChannel.Manual,
        Content = "Yazıcı arızası",
        Status = MessageStatus.New,
        ReceivedAt = 1_700_000_000
    };

    private static Ticket NewTicket(string customerId) => new()
    {
        Id = $"tkt_{Guid.NewGuid():N}",
        OwnerId = "local-user",
        CustomerId = customerId,
        Title = "Yazıcı arızası",
        Description = "Yazıcı çalışmıyor.",
        Type = "Hata",
        Priority = TicketPriority.High,
        LabelsJson = "[]",
        SubTasksJson = "[]",
        AttachmentsJson = "[]",
        AnalysisMode = AnalysisMode.Local,
        Target = TicketTarget.Jira,
        DestinationId = "IT",
        Status = TicketStatus.Draft,
        CreatedAt = 1_700_000_000,
        UpdatedAt = 1_700_000_000
    };
}

public sealed class SessionRepositoryTests
{
    [Fact]
    public async Task Trimming_keeps_newest_sessions()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var user = new User
        {
            Id = "usr_test",
            Name = "Test",
            Email = "test@example.com",
            PasswordSalt = "00",
            PasswordHash = "00",
            Role = UserRole.Admin,
            Status = UserStatus.Active,
            CreatedAt = 1
        };
        await database.Db.Users.AddAsync(user);
        await database.Db.SaveChangesAsync();

        var repository = new SessionRepository(database.Db);
        for (var index = 0; index < 4; index++)
        {
            await repository.AddAsync(new Session
            {
                Id = $"ses_{index}",
                UserId = user.Id,
                TokenHash = $"{index:x64}",
                ExpiresAt = 2_000_000_000,
                CreatedAt = index
            });
        }

        await repository.TrimAsync(user.Id, 2);

        var remaining = await database.Db.Sessions.OrderBy(session => session.CreatedAt).Select(session => session.Id).ToListAsync();
        Assert.Equal(new[] { "ses_2", "ses_3" }, remaining);
    }
}

public sealed class MailSenderServiceTests
{
    [Fact]
    public async Task Allowed_senders_include_env_and_saved_domains_and_respect_active_flag()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var clock = new TestClock();
        var settings = new SettingRepository(database.Db, new EncryptionService(database.Runtime));
        var service = new MailSenderService(new MailSenderRepository(database.Db), settings, database.Runtime, clock, new IdGenerator());

        Assert.NotNull(await service.GetAllowedAsync("USER@EXAMPLE.COM"));
        Assert.NotNull(await service.GetAllowedAsync("anyone@example.org"));
        Assert.Null(await service.GetAllowedAsync("someone@other.test"));

        await service.SaveAsync("blocked@example.com", "Engelli", active: false);
        Assert.Null(await service.GetAllowedAsync("blocked@example.com"));

        await service.SaveAsync("allowed@example.com", "İzinli", active: true);
        var allowed = await service.GetAllowedAsync("allowed@example.com");
        Assert.NotNull(allowed);
        Assert.Equal("İzinli", allowed!.Name);
    }

    [Fact]
    public async Task Invalid_domains_are_rejected()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var settings = new SettingRepository(database.Db, new EncryptionService(database.Runtime));
        var service = new MailSenderService(new MailSenderRepository(database.Db), settings, database.Runtime, new TestClock(), new IdGenerator());

        await Assert.ThrowsAsync<TickeTask.Application.Common.ApiException>(() => service.AddDomainAsync("not a domain"));
    }
}
