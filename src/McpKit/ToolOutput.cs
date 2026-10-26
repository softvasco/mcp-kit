using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    public static string Format(EndpointToolResponse response, ToolRedaction? redaction = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        return ReadJson(response, out var json)
            ? JsonSerializer.Serialize(redaction is null ? json : redaction.Apply(json), Options)
            : JsonSerializer.Serialize(response.Body, Options);
    }

    // anything that isn't valid JSON goes back as one string, so free text is never unquoted
    private static bool ReadJson(EndpointToolResponse response, out JsonNode? json)
    {
        json = null;
        if (!IsJson(response.ContentType) || string.IsNullOrWhiteSpace(response.Body))
        {
            return false;
        }

        try
        {
            json = JsonNode.Parse(response.Body);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJson(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var media)
        && media.MediaType is { } type
        && (type.Equals("application/json", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
