using Microsoft.AspNetCore.Routing;

namespace McpKit;

/// <summary>An endpoint that opted in as a tool, with what an agent needs to know about it.</summary>
public sealed record EndpointTool(string Name, string? Description, string HttpMethod, RouteEndpoint Endpoint)
{
    public string RoutePattern => Endpoint.RoutePattern.RawText ?? string.Empty;
}
