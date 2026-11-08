using ModelContextProtocol.Client;

namespace McpKit.Testing;

/// <summary>Connects an MCP client to a host running in the test, the way an agent would, with no model in between.</summary>
public static class McpTestClient
{
    /// <summary>
    /// Connects over the given client, usually from <c>TestServer.CreateClient()</c> or <c>WebApplicationFactory.CreateClient()</c>.
    /// The MCP client takes ownership of it, so disposing the MCP client disposes it too.
    /// </summary>
    public static async Task<McpClient> ConnectAsync(HttpClient http, string path = "/mcp", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (http.BaseAddress is null)
        {
            throw new ArgumentException("The HttpClient needs a BaseAddress pointing at the host.", nameof(http));
        }

        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress, path) };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: cancellationToken);
    }
}
