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
}
