using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace McpKit;

/// <summary>Input schema for a tool: route parameters plus the body's properties.</summary>
internal static class ToolInputSchema
{
    // web naming like the endpoint, but numbers as numbers: a model should not send "25" for 25
    private static readonly JsonSerializerOptions Json = CreateOptions();

    public static JsonElement For(RouteEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var properties = new JsonObject();
        var required = new List<string>();

        var bodyType = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType;
        if (bodyType is not null && Json.GetJsonSchemaAsNode(bodyType) is JsonObject body)
        {
            foreach (var (name, property) in body["properties"]?.AsObject() ?? [])
            {
                properties[name] = property?.DeepClone();
            }

            required.AddRange(body["required"]?.AsArray().Select(n => n!.GetValue<string>()) ?? []);
        }

        // route values win over a body property with the same name, so they go in last
        var handler = endpoint.Metadata.GetMetadata<MethodInfo>()?.GetParameters() ?? [];
        foreach (var parameter in endpoint.RoutePattern.Parameters)
        {
            var type = handler.FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))?.ParameterType;
            properties[parameter.Name] = RouteValueSchema(type);
            if (!parameter.IsOptional && !required.Contains(parameter.Name, StringComparer.Ordinal))
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. required.Select(r => JsonValue.Create(r))]),
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }

    // a type bound through TryParse has no JSON shape, and the value comes as text anyway
    private static JsonObject RouteValueSchema(Type? type)
    {
        if (type is null)
        {
            return new JsonObject { ["type"] = "string" };
        }

        var schema = Json.GetJsonSchemaAsNode(Nullable.GetUnderlyingType(type) ?? type);
        return schema is JsonObject node && node["type"]?.GetValueKind() == JsonValueKind.String && (string?)node["type"] != "object"
            ? node
            : new JsonObject { ["type"] = "string" };
    }
}
