namespace McpKit;

/// <summary>Tells subscribed clients that a resource changed. Call it after the write.</summary>
public interface IMcpResourceNotifier
{
    /// <summary>Notifies every session subscribed to the URI, as in api://accounts/42.</summary>
    ValueTask NotifyUpdatedAsync(string uri, CancellationToken cancellationToken = default);
}
