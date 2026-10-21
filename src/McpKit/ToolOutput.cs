using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace McpKit;

/// <summary>The text a model reads for an endpoint's response: always one JSON value.</summary>
internal static class ToolOutput
{
    // escapes < > & and quotes but keeps "João" readable, so a value can't close a tag around results
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Format(EndpointToolResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return ReadJson(response) is { } json
            ? JsonSerializer.Serialize(json, Options)
            : JsonSerializer.Serialize(response.Body, Options);
    }

    // anything that isn't valid JSON goes back as one string, so free text is never unquoted
    private static JsonElement? ReadJson(EndpointToolResponse response)
    {
        if (!IsJson(response.ContentType) || string.IsNullOrWhiteSpace(response.Body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var media)
        && media.MediaType is { } type
        && (type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
