using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace McpKit;

/// <summary>Input schema for a tool: route and query parameters plus the body's properties.</summary>
internal static class ToolInputSchema
{
    // web naming like the endpoint, but numbers as numbers: a model should not send "25" for 25
    private static readonly JsonSerializerOptions Json = CreateOptions();
    private static readonly NullabilityInfoContext Nullability = new();

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

        if (!EndpointToolInvoker.CarriesBody(endpoint))
        {
            var routeNames = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in handler.Where(p => p.Name is not null && !routeNames.Contains(p.Name)))
            {
                if (QueryName(parameter) is not { } name)
                {
                    continue;
                }

                properties[name] = QueryValueSchema(parameter.ParameterType);
                if (IsRequired(parameter) && !required.Contains(name, StringComparer.Ordinal))
                {
                    required.Add(name);
                }
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

    // mirrors what minimal APIs bind from the query string when nothing says otherwise
    private static string? QueryName(ParameterInfo parameter)
    {
        var attributes = parameter.GetCustomAttributes(inherit: true);
        if (attributes.OfType<IFromQueryMetadata>().FirstOrDefault() is { } fromQuery)
        {
            return fromQuery.Name ?? parameter.Name;
        }

        var boundElsewhere = attributes.Any(a => a is IFromServiceMetadata or IFromHeaderMetadata or IFromBodyMetadata
            or IFromFormMetadata or IFromRouteMetadata or AsParametersAttribute or FromKeyedServicesAttribute);
        return boundElsewhere || !IsQueryValue(parameter.ParameterType) ? null : parameter.Name;
    }

    private static bool IsQueryValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            return type.GetElementType() is { } element && IsQueryValue(element);
        }

        return type.IsEnum || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>));
    }

    private static JsonObject QueryValueSchema(Type type) =>
        type.IsArray && type != typeof(string)
            ? new JsonObject { ["type"] = "array", ["items"] = RouteValueSchema(type.GetElementType()) }
            : RouteValueSchema(type);

    private static bool IsRequired(ParameterInfo parameter) =>
        !parameter.HasDefaultValue
        && Nullable.GetUnderlyingType(parameter.ParameterType) is null
        && (parameter.ParameterType.IsValueType || Nullability.Create(parameter).WriteState != NullabilityState.Nullable);

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
