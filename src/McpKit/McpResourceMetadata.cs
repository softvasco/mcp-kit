namespace McpKit;

/// <summary>Marks a GET endpoint as an MCP resource. Set it with AsMcpResource.</summary>
public sealed class McpResourceMetadata(string? name = null)
{
    public string? Name { get; } = name;
}
