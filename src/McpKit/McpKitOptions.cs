namespace McpKit;

public sealed class McpKitOptions
{
    /// <summary>Where the MCP endpoint is mapped. Agents connect here.</summary>
    public string Path { get; set; } = "/mcp";

    // stateless by default: no session to pin, so the host scales out like any other API
    public bool Stateless { get; set; } = true;

    // off by default: a host exposes reads only until someone decides agents may change things
    public bool AllowWriteTools { get; set; }

    // no default: the right number depends on the API, and a guess blocks real work or does nothing
    public ToolRateLimit? RateLimit { get; set; }

    /// <summary>Fields masked in every tool's output, such as iban, name or email.</summary>
    public ToolRedaction Redaction { get; } = new();
}
