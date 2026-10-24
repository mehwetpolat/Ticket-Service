using TickeTask.Application.Common;
using TickeTask.Domain.Models;

namespace TickeTask.Tests;

public sealed class MailConfigTests
{
    [Fact]
    public async Task Graph_config_sets_defaults_and_masks_secrets()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "graph-inbox",
                    Provider = MailProviderKind.MicrosoftGraph,
                    User = "it@example.com",
                    TenantId = "contoso.onmicrosoft.com",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }
        });

        var stored = await harness.MailConfig.GetAsync();
        var mailbox = Assert.Single(stored!.Mailboxes);
        Assert.Equal("graph.microsoft.com", mailbox.Host);
        Assert.Equal(443, mailbox.Port);
        Assert.True(mailbox.Secure);
        Assert.Equal("inbox", mailbox.Mailbox);

        var admin = await harness.MailConfig.GetForAdminAsync();
        var masked = Assert.Single(admin!.Mailboxes);
        Assert.Equal(MailProviderKind.MicrosoftGraph, masked.Provider);
        Assert.Equal(string.Empty, masked.ClientSecret);
        Assert.True(masked.HasClientSecret);
        Assert.True(masked.HasCredentials);
        Assert.Equal(string.Empty, masked.Password);
        Assert.False(masked.HasPassword);
    }

    [Fact]
    public async Task Provider_hosts_are_forced_and_graph_folder_is_normalized()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "graph-inbox",
                    Provider = MailProviderKind.MicrosoftGraph,
                    Host = "imap.example.com",
                    Port = 993,
                    Secure = false,
                    User = "it@example.com",
                    Mailbox = "SENTITEMS",
                    TenantId = "contoso.onmicrosoft.com",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                },
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    Host = "imap.gmail.com",
                    Port = 993,
                    Secure = false,
                    User = "it@example.com",
                    Mailbox = "inbox",
                    ServiceAccountJson = """{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n"}"""
                }
            }
        });

        var stored = await harness.MailConfig.GetAsync();
        var graph = Assert.Single(stored!.Mailboxes, mailbox => mailbox.Provider == MailProviderKind.MicrosoftGraph);
        Assert.Equal("graph.microsoft.com", graph.Host);
        Assert.Equal(443, graph.Port);
        Assert.True(graph.Secure);
        Assert.Equal("sentitems", graph.Mailbox);

        var gmail = Assert.Single(stored.Mailboxes, mailbox => mailbox.Provider == MailProviderKind.Gmail);
        Assert.Equal("gmail.googleapis.com", gmail.Host);
        Assert.Equal(443, gmail.Port);
        Assert.True(gmail.Secure);
        Assert.Equal("INBOX", gmail.Mailbox);
    }

    [Fact]
    public async Task Gmail_rejects_non_object_and_unsafe_service_account_json()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        var arrayError = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    ServiceAccountJson = "[1,2,3]"
                }
            }
        }));
        Assert.Contains("geçerli değil", arrayError.Message);

        var tokenError = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    ServiceAccountJson = """{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n","token_uri":"http://example.com/token"}"""
                }
            }
        }));
        Assert.Contains("HTTPS", tokenError.Message);
    }

    [Fact]
    public async Task Gmail_requires_a_valid_user_email()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "not-an-email",
                    ServiceAccountJson = """{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n"}"""
                }
            }
        }));

        Assert.Contains("e-posta adresi", error.Message);
    }

    [Fact]
    public async Task Switching_provider_does_not_copy_secrets_across_providers()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "shared",
                    Provider = MailProviderKind.Imap,
                    Host = "imap.example.com",
                    User = "it@example.com",
                    Password = "imap-secret",
                    Mailbox = "INBOX"
                }
            }
        });

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "shared",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    Mailbox = "INBOX",
                    ClientId = "gmail-client"
                }
            }
        });

        var admin = await harness.MailConfig.GetForAdminAsync();
        var mailbox = Assert.Single(admin!.Mailboxes);
        Assert.False(mailbox.HasPassword);
        Assert.False(mailbox.HasClientSecret);
        Assert.False(mailbox.HasRefreshToken);
        Assert.False(mailbox.HasServiceAccount);
    }

    [Fact]
    public async Task Gmail_service_account_config_is_accepted_and_masked()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        const string serviceAccount = """{"client_email":"mail@project.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----\nkey\n-----END PRIVATE KEY-----\n"}""";

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    ServiceAccountJson = serviceAccount
                }
            }
        });

        var stored = await harness.MailConfig.GetAsync();
        var mailbox = Assert.Single(stored!.Mailboxes);
        Assert.Equal("gmail.googleapis.com", mailbox.Host);
        Assert.Equal(443, mailbox.Port);
        Assert.Equal("INBOX", mailbox.Mailbox);
        Assert.Equal(serviceAccount, mailbox.ServiceAccountJson);

        var admin = await harness.MailConfig.GetForAdminAsync();
        var masked = Assert.Single(admin!.Mailboxes);
        Assert.Equal(string.Empty, masked.ServiceAccountJson);
        Assert.True(masked.HasServiceAccount);
        Assert.True(masked.HasCredentials);
    }

    [Fact]
    public async Task Secrets_are_preserved_when_blank_values_are_saved_again()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();
        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "graph-inbox",
                    Provider = MailProviderKind.MicrosoftGraph,
                    Label = "IT",
                    User = "it@example.com",
                    TenantId = "contoso.onmicrosoft.com",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }
        });

        var config = await harness.MailConfig.GetAsync();
        config!.Mailboxes[0].Label = "IT Destek";
        config.Mailboxes[0].ClientSecret = string.Empty;
        await harness.MailConfig.SaveAsync(config);

        var stored = await harness.MailConfig.GetAsync();
        Assert.Equal("IT Destek", stored!.Mailboxes[0].Label);
        Assert.Equal("client-secret", stored.Mailboxes[0].ClientSecret);
    }

    [Fact]
    public async Task Graph_config_requires_tenant_and_client_credentials()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        var missingTenant = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "graph-inbox",
                    Provider = MailProviderKind.MicrosoftGraph,
                    User = "it@example.com",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }
        }));
        Assert.Contains("kimlik bilgileri", missingTenant.Message);

        var missingSecret = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "graph-inbox",
                    Provider = MailProviderKind.MicrosoftGraph,
                    User = "it@example.com",
                    TenantId = "contoso.onmicrosoft.com",
                    ClientId = "client-id"
                }
            }
        }));
        Assert.Contains("kimlik bilgileri", missingSecret.Message);
    }

    [Fact]
    public async Task Gmail_config_requires_refresh_token_when_no_service_account()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = true,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }
        }));

        Assert.Contains("kimlik bilgileri", error.Message);
    }

    [Fact]
    public async Task Gmail_rejects_invalid_service_account_json()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => harness.MailConfig.SaveAsync(new MailOptions
        {
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "gmail-inbox",
                    Provider = MailProviderKind.Gmail,
                    User = "it@example.com",
                    ServiceAccountJson = "{\"client_email\":\"mail@example.com\"}"
                }
            }
        }));

        Assert.Contains("client_email", error.Message);
    }

    [Fact]
    public async Task Disabled_mailbox_may_be_saved_without_credentials()
    {
        await using var harness = await MailIngestionHarness.CreateAsync();

        await harness.MailConfig.SaveAsync(new MailOptions
        {
            Enabled = false,
            Mailboxes =
            {
                new MailboxOptions
                {
                    Id = "imap-inbox",
                    Provider = MailProviderKind.Imap,
                    Host = "imap.example.com",
                    User = "it@example.com",
                    Mailbox = "INBOX"
                }
            }
        });

        var stored = await harness.MailConfig.GetAsync();
        Assert.False(stored!.Enabled);
        Assert.Equal(string.Empty, stored.Mailboxes[0].Password);
    }
}
