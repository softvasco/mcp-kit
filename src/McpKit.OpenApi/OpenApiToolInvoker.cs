using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace McpKit.OpenApi;

/// <summary>Turns a tool call into an HTTP request to the API the document describes.</summary>
public static class OpenApiToolInvoker
{
    public static Task<EndpointToolResponse> InvokeAsync(
        OpenApiTool tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpClient client,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(tool, arguments, client, caller: null, cancellationToken);

    /// <summary>Sends the call for this caller, so credentials can be picked for them.</summary>
    public static async Task<EndpointToolResponse> InvokeAsync(
        OpenApiTool tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpClient client,
        ClaimsPrincipal? caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(client);

        var missing = tool.Parameters.FirstOrDefault(p => p.Required && !arguments.ContainsKey(p.Name));
        if (missing is not null)
        {
            return Problem($"Argument '{missing.Name}' is required.");
        }

        using var request = new HttpRequestMessage(tool.Method, RelativeUri(tool, arguments));
        foreach (var header in tool.Parameters.Where(p => p.In == OpenApiParameterLocation.Header && arguments.ContainsKey(p.Name)))
        {
            request.Headers.TryAddWithoutValidation(header.Name, Text(arguments[header.Name]));
        }

        if (Body(tool, arguments) is { } body)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (caller is not null)
        {
            request.Options.Set(UpstreamCredentialsHandler.Caller, caller);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new EndpointToolResponse((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), text);
    }

    private static string RelativeUri(OpenApiTool tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        // relative to the client's base address, which may carry a path prefix of its own
        var path = new StringBuilder(tool.PathTemplate.TrimStart('/'));
        foreach (var parameter in tool.Parameters.Where(p => p.In == OpenApiParameterLocation.Path))
        {
            // escaped, so an id like "../admin" can't walk out of the template
            path.Replace($"{{{parameter.Name}}}", Uri.EscapeDataString(Text(arguments[parameter.Name])));
        }

        var query = tool.Parameters
            .Where(p => p.In == OpenApiParameterLocation.Query && arguments.ContainsKey(p.Name))
            .SelectMany(p => QueryValues(arguments[p.Name]).Select(v => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(v)}"))
            .ToList();

        return query.Count == 0 ? path.ToString() : $"{path}?{string.Join('&', query)}";
    }

    private static string? Body(OpenApiTool tool, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        switch (tool.Body)
        {
            case OpenApiToolBody.Whole:
                return arguments.TryGetValue(OpenApiToolCatalog.BodyArgument, out var whole) ? whole.GetRawText() : null;
            case OpenApiToolBody.Properties:
                var parameterNames = tool.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var properties = arguments.Where(a => !parameterNames.Contains(a.Key)).ToDictionary();
                return JsonSerializer.Serialize(properties);
            default:
                return null;
        }
    }

    // an array goes out as tags=a&tags=b, the form style OpenAPI uses for query parameters by default
    private static IEnumerable<string> QueryValues(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(Text)
        : [Text(value)];

    // strings go in as they are, anything else as its JSON text, so 25 stays 25 and true stays true
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : value.GetRawText();

    private static EndpointToolResponse Problem(string detail) => new(
        400,
        "application/problem+json",
        JsonSerializer.Serialize(new { title = "The tool call is missing an argument.", status = 400, detail, code = "tool.argument_missing" }));
}
