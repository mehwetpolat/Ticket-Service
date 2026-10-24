using System.Security.Cryptography;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Infrastructure.Security;

namespace TickeTask.Tests;

public sealed class EncryptionServiceTests
{
    [Fact]
    public void Ciphertext_round_trips_and_is_not_deterministic()
    {
        var service = new EncryptionService(TestRuntime.Create());

        var first = service.Encrypt("gizli değer");
        var second = service.Encrypt("gizli değer");

        Assert.True(service.Available);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("gizli", first);
        Assert.Equal("gizli değer", service.Decrypt(first));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var service = new EncryptionService(TestRuntime.Create());
        var encrypted = service.Encrypt("gizli değer");
        var replacement = encrypted[^1] == '0' ? '1' : '0';
        var tampered = encrypted[..^1] + replacement;

        Assert.ThrowsAny<CryptographicException>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Invalid_master_key_blocks_encryption()
    {
        var runtime = TestRuntime.Create();
        runtime.MasterKey = "kısa";
        var service = new EncryptionService(runtime);

        Assert.ThrowsAny<Exception>(() => service.Encrypt("x"));
    }
}

public sealed class SecureTokenServiceTests
{
    private readonly SecureTokenService _tokens = new();

    [Fact]
    public void Tokens_are_hex_and_have_expected_entropy()
    {
        var token = _tokens.CreateToken();

        Assert.Equal(64, token.Length);
        Assert.True(_tokens.ValidSessionToken(token));
        Assert.NotEqual(token, _tokens.CreateToken());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    public void Invalid_session_tokens_are_rejected(string? token) =>
        Assert.False(_tokens.ValidSessionToken(token));

    [Fact]
    public void Sha256_is_stable_and_lowercase()
    {
        var hash = _tokens.Sha256("token");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, _tokens.Sha256("token"));
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    [Fact]
    public void Constant_time_comparison_requires_equal_non_empty_values()
    {
        Assert.True(_tokens.ConstantTimeEquals("abc", "abc"));
        Assert.False(_tokens.ConstantTimeEquals("abc", "abd"));
        Assert.False(_tokens.ConstantTimeEquals("", ""));
        Assert.False(_tokens.ConstantTimeEquals("abc", "abcd"));
    }
}

public sealed class RateLimiterTests
{
    [Fact]
    public void Limit_blocks_after_threshold_and_keeps_keys_independent()
    {
        var limiter = new RateLimiter();

        Assert.True(limiter.Check("a", 2, 60).Allowed);
        Assert.True(limiter.Check("a", 2, 60).Allowed);
        var blocked = limiter.Check("a", 2, 60);
        Assert.False(blocked.Allowed);
        Assert.InRange(blocked.RetryAfter, 1, 60);
        Assert.True(limiter.Check("b", 2, 60).Allowed);
    }
}

public sealed class PasswordServiceTests
{
    [Fact]
    public async Task Created_password_verifies_and_wrong_password_fails()
    {
        var service = new PasswordService(new InMemorySettingRepository(), TestRuntime.Create());
        var record = await service.CreateAsync("doğru-şifre-123");

        Assert.True(await service.VerifyAsync("doğru-şifre-123", record.Salt, record.Hash));
        Assert.False(await service.VerifyAsync("yanlış-şifre-123", record.Salt, record.Hash));
    }

    [Fact]
    public async Task Setup_password_falls_back_to_runtime_secret_until_admin_exists()
    {
        var runtime = TestRuntime.Create();
        var service = new PasswordService(new InMemorySettingRepository(), runtime);

        Assert.True(await service.ValidSetupPasswordAsync(runtime.AccessPassword));
        Assert.False(await service.ValidSetupPasswordAsync("başka-şifre"));
    }

    [Fact]
    public async Task Setup_password_ignores_surrounding_whitespace()
    {
        var runtime = TestRuntime.Create();
        var service = new PasswordService(new InMemorySettingRepository(), runtime);

        Assert.True(await service.ValidSetupPasswordAsync($"  {runtime.AccessPassword}\r\n"));
    }

    [Fact]
    public async Task Generated_hex_setup_password_is_case_insensitive()
    {
        var runtime = TestRuntime.Create();
        runtime.AccessPassword = "874fd920d8a6557e0138e5a0";
        var service = new PasswordService(new InMemorySettingRepository(), runtime);

        Assert.True(await service.ValidSetupPasswordAsync("874FD920D8A6557E0138E5A0"));
    }

    [Fact]
    public async Task File_setup_secret_wins_over_a_stale_admin_password_setting()
    {
        var settings = new InMemorySettingRepository();
        var runtime = TestRuntime.Create();
        var service = new PasswordService(settings, runtime);
        var stale = await service.CreateAsync("eski-kurulum-sifresi");
        await settings.SetAsync("local-user", "admin_password", new { salt = stale.Salt, hash = stale.Hash });

        Assert.True(await service.ValidSetupPasswordAsync(runtime.AccessPassword));
    }

    [Fact]
    public async Task Dummy_verification_always_fails()
    {
        var service = new PasswordService(new InMemorySettingRepository(), TestRuntime.Create());
        Assert.False(await service.VerifyDummyAsync("anything"));
    }
}

public sealed class SettingsValidationTests
{
    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://ollama:11434")]
    [InlineData("http://192.168.1.10:11434")]
    [InlineData("http://10.0.0.4:11434")]
    [InlineData("http://172.20.0.2:11434")]
    public void Llm_endpoint_accepts_private_http_hosts(string endpoint) =>
        Assert.Equal(endpoint, TickeTask.Infrastructure.Services.SettingsService.NormalizeLlmEndpoint(endpoint));

    [Theory]
    [InlineData("https://127.0.0.1:11434")]
    [InlineData("http://example.com:11434")]
    [InlineData("http://127.0.0.1:11434/api")]
    [InlineData("http://user:pass@127.0.0.1:11434")]
    public void Llm_endpoint_rejects_public_or_decorated_hosts(string endpoint) =>
        Assert.Throws<ApiException>(() => TickeTask.Infrastructure.Services.SettingsService.NormalizeLlmEndpoint(endpoint));

    [Fact]
    public void Jira_base_url_requires_https_and_no_credentials()
    {
        Assert.Equal("https://acme.atlassian.net", TickeTask.Infrastructure.Services.SettingsService.NormalizeJiraBaseUrl("https://acme.atlassian.net/"));
        Assert.Throws<ApiException>(() => TickeTask.Infrastructure.Services.SettingsService.NormalizeJiraBaseUrl("http://acme.atlassian.net"));
        Assert.Throws<ApiException>(() => TickeTask.Infrastructure.Services.SettingsService.NormalizeJiraBaseUrl("https://user:pass@acme.atlassian.net"));
    }

    [Fact]
    public void Api_base_url_requires_https_and_strips_trailing_slash()
    {
        Assert.Equal("https://ghe.example.com/api/v3", TickeTask.Infrastructure.Services.SettingsService.NormalizeApiBaseUrl("https://ghe.example.com/api/v3/", ""));
        Assert.Equal("https://api.github.com", TickeTask.Infrastructure.Services.SettingsService.NormalizeApiBaseUrl("", "https://api.github.com"));
        Assert.Throws<ApiException>(() => TickeTask.Infrastructure.Services.SettingsService.NormalizeApiBaseUrl("http://ghe.example.com", ""));
    }
}
