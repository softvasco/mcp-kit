namespace McpKit;

/// <summary>A tool argument that reaches the endpoint as a request header.</summary>
public sealed class McpToolHeaderMetadata
{
    public McpToolHeaderMetadata(string header, string argument, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Header = header;
        Argument = argument;
        Description = description;
    }

    public string Header { get; }

    public string Argument { get; }

    /// <summary>Shown to the model in the tool schema, so say when and how to send it.</summary>
    public string Description { get; }
}
