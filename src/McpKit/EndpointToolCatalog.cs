using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace McpKit;

/// <summary>Finds the endpoints marked with <see cref="McpToolMetadata"/>. Mistakes in the markings fail here, at startup, not on a tool call.</summary>
public sealed class EndpointToolCatalog(EndpointDataSource endpoints)
{
    public IReadOnlyList<EndpointTool> Discover()
    {
        var tools = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<McpToolMetadata>() is not null)
            .Select(ToTool)
            .ToList();

        var duplicate = tools.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tool name '{duplicate.Key}' is used by {string.Join(" and ", duplicate.Select(t => $"{t.HttpMethod} {t.RoutePattern}"))}.");
        }

        return tools;
    }

    private static EndpointTool ToTool(RouteEndpoint endpoint)
    {
        var route = endpoint.RoutePattern.RawText;
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

        // a tool is one operation; an endpoint that answers several verbs has no single meaning to describe
        if (methods.Count != 1)
        {
            throw new InvalidOperationException($"Endpoint '{route}' is marked as a tool but maps {methods.Count} HTTP methods; tools need exactly one.");
        }

        var name = endpoint.Metadata.GetMetadata<McpToolMetadata>()!.Name
            ?? endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? throw new InvalidOperationException(
                $"Endpoint '{methods[0]} {route}' is marked as a tool but has no name. Add WithName or pass a name to AsMcpTool.");

        var description = endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary
            ?? endpoint.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description;

        return new EndpointTool(JsonNamingPolicy.SnakeCaseLower.ConvertName(name), description, methods[0], endpoint);
    }
}
