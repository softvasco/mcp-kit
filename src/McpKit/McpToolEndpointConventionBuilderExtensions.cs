using Microsoft.AspNetCore.Builder;

namespace McpKit;

public static class McpToolEndpointConventionBuilderExtensions
{
    /// <summary>Publishes this endpoint as an MCP tool. Endpoints without it are never exposed.</summary>
    public static TBuilder AsMcpTool<TBuilder>(this TBuilder builder, string? name = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolMetadata(name));
    }

    /// <summary>Adds an optional tool argument that the endpoint gets as a request header.</summary>
    public static TBuilder WithMcpToolHeader<TBuilder>(this TBuilder builder, string header, string argument, string description)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolHeaderMetadata(header, argument, description));
    }
}
