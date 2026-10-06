namespace McpKit;

public sealed class McpKitOptions
{
    /// <summary>Where the MCP endpoint is mapped. Agents connect here.</summary>
    public string Path { get; set; } = "/mcp";

    // stateless by default: no session to pin, so the host scales out like any other API
    public bool Stateless { get; set; } = true;
}
