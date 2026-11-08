namespace McpKit.Testing;

/// <summary>The tools a server lists differ from the snapshot kept for them.</summary>
public sealed class McpToolSnapshotException : Exception
{
    public McpToolSnapshotException()
    {
    }

    public McpToolSnapshotException(string message)
        : base(message)
    {
    }

    public McpToolSnapshotException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
