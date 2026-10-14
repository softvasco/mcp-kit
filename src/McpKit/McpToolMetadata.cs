namespace McpKit;

/// <summary>Marks an endpoint as an MCP tool. Added by <see cref="McpToolEndpointConventionBuilderExtensions.AsMcpTool{TBuilder}"/>.</summary>
public sealed class McpToolMetadata(string? name = null)
{
    /// <summary>The tool name, or null to use the endpoint name in snake_case.</summary>
    public string? Name { get; } = name;
}
