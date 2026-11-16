using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

internal sealed partial class ResourceSubscriptions(ILogger<ResourceSubscriptions> logger) : IMcpResourceNotifier
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    /// <summary>Runs a stateful session and forgets its subscriptions when it ends.</summary>
    public async Task RunAsync(McpServer server, CancellationToken cancellationToken)
    {
        // request handlers get a wrapper bound to their own request, so the session is keyed by id
        // and notifications go through the server that outlives every request
        var id = server.SessionId ?? throw new InvalidOperationException("A stateful MCP session has no id.");
        _sessions[id] = new Session(server);
        try
        {
            await server.RunAsync(cancellationToken);
        }
        finally
        {
            _sessions.TryRemove(id, out _);
        }
    }

    public void Subscribe(McpServer request, string uri)
    {
        if (request.SessionId is not { } id || !_sessions.TryGetValue(id, out var session))
        {
            throw new McpProtocolException("Subscriptions need a stateful session.", McpErrorCode.InvalidRequest);
        }

        session.Uris.TryAdd(uri, 0);
    }

    public void Unsubscribe(McpServer request, string uri)
    {
        if (request.SessionId is { } id && _sessions.TryGetValue(id, out var session))
        {
            session.Uris.TryRemove(uri, out _);
        }
    }

    public async ValueTask NotifyUpdatedAsync(string uri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        foreach (var session in _sessions.Values)
        {
            if (!session.Uris.ContainsKey(uri))
            {
                continue;
            }

            try
            {
                await session.Server.SendNotificationAsync(
                    NotificationMethods.ResourceUpdatedNotification,
                    new ResourceUpdatedNotificationParams { Uri = uri },
                    McpJsonUtilities.DefaultOptions,
                    cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // one client that can't be reached shouldn't stop the others hearing about the change
                LogNotifyFailed(ex, uri);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not notify a session about {Uri}.")]
    private partial void LogNotifyFailed(Exception exception, string uri);

    private sealed record Session(McpServer Server)
    {
        public ConcurrentDictionary<string, byte> Uris { get; } = new(StringComparer.Ordinal);
    }
}
