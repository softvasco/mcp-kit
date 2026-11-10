using Microsoft.AspNetCore.Routing;

namespace McpKit;

/// <summary>A GET endpoint that opted in as a resource, read by URI instead of called.</summary>
public sealed record EndpointResource(string Name, string? Description, RouteEndpoint Endpoint, ResourceUriTemplate Uri);
