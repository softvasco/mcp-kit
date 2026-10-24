# Connecting from MCP clients

McpKit serves MCP over streamable HTTP at `/mcp`, stateless by default. Any client that speaks that transport can connect with just the URL. The examples use the account sample on a fixed port:

```bash
dotnet run --project samples/McpKit.LedgerSample --urls http://localhost:5080
```

## VS Code

Add the server to `.vscode/mcp.json` in the workspace (or to the user-level MCP config to have it everywhere):

```json
{
  "servers": {
    "ledger": {
      "type": "http",
      "url": "http://localhost:5080/mcp"
    }
  }
}
```

VS Code shows a Start action above the entry. Once it's running, `get_account` and `get_statement` show up in the tool picker of agent mode, with the endpoint summaries as their descriptions.

If `/mcp` needs a token, pass it as a header and let VS Code ask for it, so it doesn't end up in the repo:

```json
{
  "inputs": [
    { "type": "promptString", "id": "ledger-token", "description": "Ledger API token", "password": true }
  ],
  "servers": {
    "ledger": {
      "type": "http",
      "url": "https://ledger.example.com/mcp",
      "headers": { "Authorization": "Bearer ${input:ledger-token}" }
    }
  }
}
```

The tool list depends on who is asking: a token without the scope an endpoint's policy requires won't see that tool at all ([ADR 0005](adr/0005-authorization-per-tool.md)). If a tool you expect is missing, check the token's scopes before the endpoint.

## MCP Inspector

The Inspector is the quickest way to see the schemas the model gets and to call a tool by hand:

```bash
npx @modelcontextprotocol/inspector
```

Pick Streamable HTTP as the transport, enter `http://localhost:5080/mcp` and connect. List Tools shows each input schema as McpKit built it from the route, query and body.

## From C#

The official SDK's client works against it directly. This is what the tests do, over `WebApplicationFactory` instead of a real port:

```csharp
var options = new HttpClientTransportOptions { Endpoint = new Uri("http://localhost:5080/mcp") };
await using var client = await McpClient.CreateAsync(new HttpClientTransport(options));

foreach (var tool in await client.ListToolsAsync())
{
    Console.WriteLine($"{tool.Name}: {tool.Description}");
}

var result = await client.CallToolAsync(
    "get_account",
    new Dictionary<string, object?> { ["id"] = "0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e01" });
```

## With curl

Useful to see the raw messages. The server is stateless, so there is no session id to carry between calls, but the `Accept` header has to allow both JSON and an event stream:

```bash
curl -s http://localhost:5080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_account","arguments":{"id":"0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e01"}}}'
```

The reply is one `message` event. The text content is the endpoint's JSON body, and the same object comes back in `structuredContent`:

```json
{"id":"0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e01","iban":"PT50000201231234567890154","currency":"EUR","status":"Open","balance":59.50}
```

## Things that trip people up

- The path is `/mcp`, not the root. Change it with `options.Path` in `AddMcpKit`, and update the clients with it.
- Write tools are only listed when the host sets `AllowWriteTools`. A client that sees only reads is usually talking to a host without it ([ADR 0004](adr/0004-read-only-by-default.md)).
- A tool behind `RequireMcpConfirmation()` returns a `confirmation_token` on the first call instead of running, and asks the agent to show the user the call. A script has to send the same call again with the token ([ADR 0006](adr/0006-confirmation-before-a-write.md)).
- Behind a load balancer, stateless mode needs nothing special. Confirmation tokens do: share the data protection key ring between instances.
