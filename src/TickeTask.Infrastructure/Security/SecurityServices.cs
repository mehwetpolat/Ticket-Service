using System.Security.Cryptography;
using System.Text;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Abstractions.Persistence;
using TickeTask.Application.Common;

namespace TickeTask.Infrastructure.Security;

public sealed class EncryptionService(AppRuntime runtime) : IEncryptionService
{
    public bool Available
    {
        get
        {
            try { return runtime.MasterKey.Length == 64 && Convert.FromHexString(runtime.MasterKey).Length == 32; }
            catch (FormatException) { return false; }
        }
    }

    public string Encrypt(string value)
    {
        if (!Available) throw ApiException.Unsupported("APP_MASTER_KEY hazır değil.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Convert.FromHexString(runtime.MasterKey), 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return $"{Convert.ToHexString(nonce).ToLowerInvariant()}:{Convert.ToHexString(cipher).ToLowerInvariant()}{Convert.ToHexString(tag).ToLowerInvariant()}";
    }

    public string Decrypt(string value)
    {
        if (!Available) throw ApiException.Unsupported("APP_MASTER_KEY hazır değil.");
        var parts = value.Split(':');
        if (parts.Length != 2) throw new CryptographicException("Şifreli ayar biçimi geçersiz.");
        var nonce = Convert.FromHexString(parts[0]);
        var payload = Convert.FromHexString(parts[1]);
        if (payload.Length < 16) throw new CryptographicException("Şifreli ayar eksik.");
        var cipher = payload[..^16];
        var tag = payload[^16..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(Convert.FromHexString(runtime.MasterKey), 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public sealed class SecureTokenService : ISecureTokenService
{
    public string CreateToken(int bytes = 32) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public bool ConstantTimeEquals(string left, string right)
    {
        if (left.Length == 0 || left.Length != right.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }

    public bool ValidSessionToken(string? token) =>
        token is { Length: 64 } && token.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class PasswordService(ISettingRepository settings, AppRuntime runtime) : IPasswordService
{
    private const int Iterations = 210000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private sealed record PasswordRecord(string Salt, string Hash);

    public Task<bool> VerifyAsync(string password, string salt, string hash) =>
        Task.FromResult(Verify(password, salt, hash));

    public Task<(string Salt, string Hash)> CreateAsync(string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltBytes)).ToLowerInvariant();
        return Task.FromResult((salt, Derive(password, salt)));
    }

    public async Task<bool> VerifyDummyAsync(string password)
    {
        await Task.Run(() => Derive(password, new string('0', 32)));
        return false;
    }

    public async Task<bool> ValidSetupPasswordAsync(string password)
    {
        var candidate = password.Trim();
        if (candidate.Length == 0) return false;

        // The file on disk is the documented setup secret. Check it first so a
        // stale admin_password setting from an interrupted/older setup cannot
        // lock the owner out of a fresh database.
        if (MatchesSetupSecret(candidate, runtime.AccessPassword)) return true;

        var record = await settings.GetAsync<PasswordRecord>("local-user", "admin_password");
        return record is not null && Verify(candidate, record.Salt, record.Hash);
    }

    private static bool MatchesSetupSecret(string candidate, string secret)
    {
        if (secret.Length == 0) return false;
        if (ConstantTimeEquals(candidate, secret)) return true;

        // Generated setup secrets are hexadecimal. Accepting either case makes
        // copy/paste from mobile or uppercase keyboards harmless; permanent
        // user passwords remain case-sensitive.
        return candidate.All(IsHexDigit) && secret.All(IsHexDigit) &&
               ConstantTimeEquals(candidate.ToLowerInvariant(), secret.ToLowerInvariant());
    }

    private static bool IsHexDigit(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool Verify(string password, string salt, string hash) =>
        ConstantTimeEquals(Derive(password, salt), hash);

    private static string Derive(string password, string salt)
    {
        var saltBytes = Convert.FromHexString(salt);
        var bytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), saltBytes, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool ConstantTimeEquals(string left, string right)
    {
        if (left.Length != right.Length || left.Length == 0) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}

public sealed class IdGenerator : IIdGenerator
{
    public string Create(string prefix) => $"{prefix}_{Guid.NewGuid()}";
}

public sealed class SystemClock : IClock
{
    public long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class RateLimiter : IRateLimiter
{
    private sealed class Bucket
    {
        public int Count;
        public long ResetAt;
    }

    private readonly Dictionary<string, Bucket> _buckets = new();
    private long _lastSweep;
    private readonly object _sync = new();

    public RateLimitResult Check(string key, int limit, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_sync)
        {
            if (now - _lastSweep >= 60_000)
            {
                _lastSweep = now;
                foreach (var expired in _buckets.Where(pair => pair.Value.ResetAt <= now).Select(pair => pair.Key).ToList())
                    _buckets.Remove(expired);
            }
            if (!_buckets.TryGetValue(key, out var bucket) || bucket.ResetAt <= now)
            {
                _buckets[key] = new Bucket { Count = 1, ResetAt = now + windowSeconds * 1000L };
                return new RateLimitResult(true, 0);
            }
            if (bucket.Count >= limit)
                return new RateLimitResult(false, Math.Max(1, (int)Math.Ceiling((bucket.ResetAt - now) / 1000.0)));
            bucket.Count++;
            return new RateLimitResult(true, 0);
        }
    }
}
