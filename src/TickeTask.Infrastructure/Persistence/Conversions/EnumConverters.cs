using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TickeTask.Domain.Enums;

namespace TickeTask.Infrastructure.Persistence.Conversions;

public static class EnumConverters
{
    public static ValueConverter<TEnum, string> LowerString<TEnum>() where TEnum : struct, Enum =>
        new(value => value.ToString().ToLowerInvariant(),
            value => Parse<TEnum>(value));

    public static ValueConverter<TEnum, string> SnakeString<TEnum>() where TEnum : struct, Enum =>
        new(value => ToSnake(value.ToString()),
            value => Parse<TEnum>(ToPascal(value)));

    private static TEnum Parse<TEnum>(string value) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed)) return parsed;
        throw new InvalidOperationException($"Bilinmeyen değer: {value}.");
    }

    private static string ToSnake(string value)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0) builder.Append('_');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    private static string ToPascal(string value) =>
        string.Concat(value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
