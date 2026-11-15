using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

internal sealed partial class ResourceSubscriptions(ILogger<ResourceSubscriptions> logger) : IMcpResourceNotifier
{
    private readonly ConcurrentDictionary<McpServer, ConcurrentDictionary<string, byte>> _sessions = new();

    public void Subscribe(McpServer session, string uri) =>
        _sessions.GetOrAdd(session, _ => new(StringComparer.Ordinal)).TryAdd(uri, 0);

    public void Unsubscribe(McpServer session, string uri)
    {
        if (_sessions.TryGetValue(session, out var uris))
        {
            uris.TryRemove(uri, out _);
        }
    }

    public async ValueTask NotifyUpdatedAsync(string uri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        foreach (var (session, uris) in _sessions)
        {
            if (!uris.ContainsKey(uri))
            {
                continue;
            }

            try
            {
                await session.SendNotificationAsync(
                    NotificationMethods.ResourceUpdatedNotification,
                    new ResourceUpdatedNotificationParams { Uri = uri },
                    McpJsonUtilities.DefaultOptions,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // the sdk gives no hook for a session ending, so a failed send is how we learn it's gone
                _sessions.TryRemove(session, out _);
                LogSessionDropped(ex, uri);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped the subscriptions of a session that could not be notified about {Uri}.")]
    private partial void LogSessionDropped(Exception exception, string uri);
}
