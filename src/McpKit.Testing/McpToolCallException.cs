namespace McpKit.Testing;

/// <summary>A tool answered with an error result.</summary>
public sealed class McpToolCallException : Exception
{
    public McpToolCallException()
    {
    }

    public McpToolCallException(string message)
        : base(message)
    {
    }

    public McpToolCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public McpToolCallException(string toolName, string errorText)
        : base($"Tool '{toolName}' returned an error: {errorText}")
    {
        ToolName = toolName;
        ErrorText = errorText;
    }

    public string? ToolName { get; }

    public string? ErrorText { get; }
}
