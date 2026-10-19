namespace McpKit;

/// <summary>Marks an endpoint as an MCP tool. Added by AsMcpTool or AsMcpWriteTool.</summary>
public sealed class McpToolMetadata(string? name = null, bool changesState = false)
{
    /// <summary>The tool name, or null to use the endpoint name in snake_case.</summary>
    public string? Name { get; } = name;

    public bool ChangesState { get; } = changesState;
}
