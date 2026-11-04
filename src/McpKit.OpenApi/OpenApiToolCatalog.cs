using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace McpKit.OpenApi;

/// <summary>Describes each operation of an OpenAPI document as a tool; bad documents fail here.</summary>
public static partial class OpenApiToolCatalog
{
    public const string BodyArgument = "body";

    public static async Task<IReadOnlyList<OpenApiTool>> LoadAsync(Stream document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // style rules don't stop a tool from working, and a document must not make us fetch URLs
        var settings = new OpenApiReaderSettings { RuleSet = ValidationRuleSet.GetEmptyRuleSet(), LoadExternalRefs = false };
        var (openApi, diagnostic) = await OpenApiDocument.LoadAsync(document, "json", settings, cancellationToken);
        if (openApi is null || diagnostic is { Errors.Count: > 0 })
        {
            var errors = diagnostic?.Errors.Select(e => $"{e.Pointer}: {e.Message}") ?? ["the document could not be read"];
            throw new InvalidOperationException($"The OpenAPI document has errors: {string.Join("; ", errors)}");
        }

        var tools = new List<OpenApiTool>();
        foreach (var (path, item) in openApi.Paths)
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                tools.Add(ToTool(path, item, method, operation));
            }
        }

        var duplicate = tools.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tool name '{duplicate.Key}' is used by {string.Join(" and ", duplicate.Select(t => $"{t.Method} {t.PathTemplate}"))}.");
        }

        return tools;
    }

    private static OpenApiTool ToTool(string path, IOpenApiPathItem item, HttpMethod method, OpenApiOperation operation)
    {
        var properties = new JsonObject();
        var required = new List<string>();
        var body = AddBody(operation.RequestBody, properties, required);

        // operation parameters override path-level ones with the same name and location
        var parameters = (item.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .Where(p => p.Name is not null && p.In is ParameterLocation.Path or ParameterLocation.Query or ParameterLocation.Header)
            .GroupBy(p => (p.Name, p.In))
            .Select(g => g.Last())
            .ToList();

        // parameters win over a body property with the same name, so they go in last
        var toolParameters = new List<OpenApiToolParameter>();
        foreach (var parameter in parameters)
        {
            var schema = OpenApiSchemaJson.ToNode(parameter.Schema);
            if (!string.IsNullOrEmpty(parameter.Description))
            {
                schema["description"] = parameter.Description;
            }

            properties[parameter.Name!] = schema;
            var isRequired = parameter.Required || parameter.In == ParameterLocation.Path;
            if (isRequired && !required.Contains(parameter.Name!, StringComparer.Ordinal))
            {
                required.Add(parameter.Name!);
            }

            toolParameters.Add(new OpenApiToolParameter(parameter.Name!, Location(parameter.In!.Value), isRequired));
        }

        var input = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
        {
            input["required"] = new JsonArray([.. required.Select(r => JsonValue.Create(r))]);
        }

        return new OpenApiTool(
            Name(operation.OperationId, method, path),
            operation.Summary ?? operation.Description,
            method,
            path,
            JsonSerializer.SerializeToElement(input),
            toolParameters,
            body);
    }

    private static OpenApiToolBody AddBody(IOpenApiRequestBody? requestBody, JsonObject properties, List<string> required)
    {
        var media = requestBody?.Content?.FirstOrDefault(c => IsJson(c.Key)).Value;
        if (media?.Schema is null)
        {
            return OpenApiToolBody.None;
        }

        var schema = OpenApiSchemaJson.ToNode(media.Schema);
        if (schema["properties"] is JsonObject bodyProperties && media.Schema.Type is JsonSchemaType.Object)
        {
            foreach (var (name, property) in bodyProperties)
            {
                properties[name] = property?.DeepClone();
            }

            required.AddRange(schema["required"]?.AsArray().Select(r => r!.GetValue<string>()) ?? []);
            return OpenApiToolBody.Properties;
        }

        properties[BodyArgument] = schema;
        if (requestBody!.Required)
        {
            required.Add(BodyArgument);
        }

        return OpenApiToolBody.Whole;
    }

    // operationId is the authors' own name; without it, verb and path still give a stable one
    private static string Name(string? operationId, HttpMethod method, string path)
    {
        var name = !string.IsNullOrWhiteSpace(operationId)
            ? operationId
            : $"{method.Method} {path}";

        var words = NonWord().Replace(JsonNamingPolicy.SnakeCaseLower.ConvertName(name), "_").Trim('_');
        return Underscores().Replace(words, "_");
    }

    private static bool IsJson(string mediaType) =>
        mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);

    private static OpenApiParameterLocation Location(ParameterLocation location) => location switch
    {
        ParameterLocation.Path => OpenApiParameterLocation.Path,
        ParameterLocation.Query => OpenApiParameterLocation.Query,
        _ => OpenApiParameterLocation.Header,
    };

    [GeneratedRegex("[^a-z0-9_]")]
    private static partial Regex NonWord();

    [GeneratedRegex("_{2,}")]
    private static partial Regex Underscores();
}
