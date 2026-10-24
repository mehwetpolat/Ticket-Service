using System.Text.Json;
using TickeTask.Application.Common;

namespace TickeTask.Infrastructure.Mail;

internal sealed record GoogleServiceAccount(string ClientEmail, string PrivateKey, string? TokenUri);

internal static class GoogleServiceAccountJson
{
    public static GoogleServiceAccount Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw ApiException.BadRequest("Google hizmet hesabı JSON dosyası geçerli değil.");
            var email = ReadString(root, "client_email");
            var key = ReadString(root, "private_key");
            var tokenUri = ReadString(root, "token_uri");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
                throw ApiException.BadRequest("Google hizmet hesabı JSON dosyasında client_email ve private_key bulunmalı.");
            if (!key.Contains("BEGIN", StringComparison.Ordinal) || !key.Contains("PRIVATE KEY", StringComparison.Ordinal))
                throw ApiException.BadRequest("Google hizmet hesabı özel anahtarı PEM biçiminde olmalı.");
            if (!string.IsNullOrWhiteSpace(tokenUri) &&
                (!Uri.TryCreate(tokenUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                throw ApiException.BadRequest("Google hizmet hesabı token_uri alanı geçerli bir HTTPS adresi olmalı.");
            return new GoogleServiceAccount(email, key, tokenUri);
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("Google hizmet hesabı JSON dosyası geçerli değil.");
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
