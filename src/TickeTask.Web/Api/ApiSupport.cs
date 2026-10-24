using System.Text.Json;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Infrastructure;

namespace TickeTask.Web.Api;

public static class ApiSupport
{
    public static IResult Json(object? value, int status = 200) =>
        Results.Json(value, JsonDefaults.Options, statusCode: status);

    public static async Task<T> Body<T>(HttpRequest request)
    {
        try
        {
            var value = await request.ReadFromJsonAsync<T>(JsonDefaults.Options);
            return value ?? throw ApiException.BadRequest("İstek gövdesi boş.");
        }
        catch (JsonException) { throw ApiException.BadRequest("İstek biçimi geçersiz."); }
        catch (BadHttpRequestException) { throw ApiException.BadRequest("İstek biçimi geçersiz."); }
    }

    public static async Task<JsonElement> RawBody(HttpRequest request)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw ApiException.BadRequest("İstek biçimi geçersiz."); }
    }

    public static void Limit(HttpRequest request, long bytes, string message)
    {
        if (request.ContentLength is > 0 && request.ContentLength > bytes) throw ApiException.TooLarge(message);
    }

    public static void RateLimited(IRateLimiter limiter, string key, int limit, int windowSeconds)
    {
        var result = limiter.Check(key, limit, windowSeconds);
        if (!result.Allowed)
            throw new ApiException("Çok fazla deneme yapıldı. Lütfen biraz sonra tekrar deneyin.", 429) { Data = { ["retryAfter"] = result.RetryAfter } };
    }

    public static IResult Error(int status, string message) => Json(new { error = message }, status);

    public static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static bool ValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 254 &&
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");
}
