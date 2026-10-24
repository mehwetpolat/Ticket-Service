using System.Text.Json;
using System.Text.Json.Serialization;
using TickeTask.Domain.Enums;

namespace TickeTask.Infrastructure;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new TickeTaskEnumConverterFactory());
        return options;
    }
}

public sealed class TickeTaskEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value)) throw new JsonException("Enum değeri boş olamaz.");
            var normalized = value.Replace("_", string.Empty).Replace("-", string.Empty);
            if (!Enum.TryParse<T>(normalized, true, out var parsed))
                throw new JsonException($"Bilinmeyen değer: {value}.");
            return parsed;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var name = value.ToString();
            writer.WriteStringValue(typeof(T) == typeof(MailIngestionStatus) && name == nameof(MailIngestionStatus.DryRun)
                ? "dry_run"
                : name.ToLowerInvariant());
        }
    }
}
