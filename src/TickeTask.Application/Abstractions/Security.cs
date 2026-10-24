namespace TickeTask.Application.Abstractions;

public interface IEncryptionService
{
    string Encrypt(string value);
    string Decrypt(string value);
    bool Available { get; }
}

public interface ISecureTokenService
{
    string CreateToken(int bytes = 32);
    string Sha256(string value);
    bool ConstantTimeEquals(string left, string right);
    bool ValidSessionToken(string? token);
}

public interface IRateLimiter
{
    RateLimitResult Check(string key, int limit, int windowSeconds);
}

public record RateLimitResult(bool Allowed, int RetryAfter);
