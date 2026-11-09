using System.Globalization;
using System.Text.Json;

namespace McpKit;

/// <summary>Checks a call's arguments against the tool's input schema before the endpoint runs.</summary>
internal static class ToolArguments
{
    // only what the generated schemas say: required names, the JSON type and the formats binding would reject
    public static Dictionary<string, string> Problems(JsonElement schema, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!arguments.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    problems[name] = "is required";
                }
            }
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return problems;
        }

        foreach (var (name, value) in arguments)
        {
            if (!problems.ContainsKey(name)
                && properties.TryGetProperty(name, out var property)
                && Mismatch(property, value) is { } problem)
            {
                problems[name] = problem;
            }
        }

        return problems;
    }

    private static string? Mismatch(JsonElement property, JsonElement value)
    {
        if (!property.TryGetProperty("type", out var type))
        {
            return null;
        }

        var types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()!).ToList()
            : [type.GetString()!];

        if (value.ValueKind == JsonValueKind.Null)
        {
            return types.Contains("null") ? null : "can't be null";
        }

        var expected = types.First(t => t != "null");
        var format = property.TryGetProperty("format", out var f) ? f.GetString() : null;

        return expected switch
        {
            "string" when value.ValueKind != JsonValueKind.String => "must be a string",
            "string" => FormatMismatch(format, value.GetString()!),
            "integer" when value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var n) || n != decimal.Truncate(n) => "must be a whole number",
            "number" when value.ValueKind != JsonValueKind.Number => "must be a number",
            "boolean" when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) => "must be true or false",
            "array" when value.ValueKind != JsonValueKind.Array => "must be an array",
            "object" when value.ValueKind != JsonValueKind.Object => "must be an object",
            _ => null,
        };
    }

    private static string? FormatMismatch(string? format, string text) => format switch
    {
        "uuid" when !Guid.TryParse(text, out _) => "must be a UUID",
        "date-time" when !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, out _) => "must be an ISO 8601 date and time",
        "date" when !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => "must be a date as yyyy-MM-dd",
        _ => null,
    };
}
