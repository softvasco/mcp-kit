using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace McpKit.OpenApi;

/// <summary>An OpenAPI schema as a self-contained JSON schema, references inlined.</summary>
internal static class OpenApiSchemaJson
{
    // deep enough for real request types; a recursive schema gets an open schema past this point
    private const int MaxDepth = 8;

    public static JsonObject ToNode(IOpenApiSchema? schema) => ToNode(schema, 0);

    private static JsonObject ToNode(IOpenApiSchema? schema, int depth)
    {
        var node = new JsonObject();
        if (schema is null || depth > MaxDepth)
        {
            return node;
        }

        if (schema.Type is { } type)
        {
            var names = TypeNames(type);
            node["type"] = names.Count == 1 ? names[0] : new JsonArray([.. names.Select(n => JsonValue.Create(n))]);
        }

        Set(node, "format", schema.Format);
        Set(node, "description", schema.Description);
        Set(node, "pattern", schema.Pattern);
        Set(node, "minLength", schema.MinLength);
        Set(node, "maxLength", schema.MaxLength);
        SetNumber(node, "minimum", schema.Minimum);
        SetNumber(node, "maximum", schema.Maximum);

        if (schema.Enum is { Count: > 0 } values)
        {
            node["enum"] = new JsonArray([.. values.Select(v => v?.DeepClone())]);
        }

        if (schema.Default is { } value)
        {
            node["default"] = value.DeepClone();
        }

        if (schema.Items is { } items)
        {
            node["items"] = ToNode(items, depth + 1);
        }

        if (schema.Properties is { Count: > 0 } properties)
        {
            node["properties"] = new JsonObject(properties.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)ToNode(p.Value, depth + 1))));
        }

        if (schema.Required is { Count: > 0 } required)
        {
            node["required"] = new JsonArray([.. required.Order(StringComparer.Ordinal).Select(r => JsonValue.Create(r))]);
        }

        Combine(node, "allOf", schema.AllOf, depth);
        Combine(node, "oneOf", schema.OneOf, depth);
        Combine(node, "anyOf", schema.AnyOf, depth);
        return node;
    }

    private static List<string> TypeNames(JsonSchemaType type) =>
        [.. Enum.GetValues<JsonSchemaType>().Where(t => t != 0 && type.HasFlag(t)).Select(t => t.ToString().ToLowerInvariant())];

    private static void Combine(JsonObject node, string keyword, IList<IOpenApiSchema>? schemas, int depth)
    {
        if (schemas is { Count: > 0 })
        {
            node[keyword] = new JsonArray([.. schemas.Select(s => (JsonNode)ToNode(s, depth + 1))]);
        }
    }

    private static void Set(JsonObject node, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            node[name] = value;
        }
    }

    private static void Set(JsonObject node, string name, int? value)
    {
        if (value is { } number)
        {
            node[name] = number;
        }
    }

    // the reader keeps minimum and maximum as the document's text, so 0.01 stays 0.01
    private static void SetNumber(JsonObject node, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            node[name] = JsonNode.Parse(value);
        }
    }
}
