namespace McpKit;

/// <summary>How many calls one client may make to one tool in each window.</summary>
public sealed class ToolRateLimit
{
    public ToolRateLimit(int permitLimit, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(permitLimit, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        PermitLimit = permitLimit;
        Window = window;
    }

    public int PermitLimit { get; }

    public TimeSpan Window { get; }
}
