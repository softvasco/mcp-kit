namespace McpKit;

/// <summary>Marks a tool that only runs on a second call carrying the token from the first.</summary>
public sealed class McpToolConfirmationMetadata
{
    public const string Argument = "confirmation_token";

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    public McpToolConfirmationMetadata(TimeSpan? lifetime = null)
    {
        Lifetime = lifetime ?? DefaultLifetime;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Lifetime, TimeSpan.Zero, nameof(lifetime));
    }

    /// <summary>How long the token from the first call stays good.</summary>
    public TimeSpan Lifetime { get; }
}
