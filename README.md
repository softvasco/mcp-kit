# McpKit

[![ci](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml)
[![license](https://img.shields.io/github/license/softvasco/mcp-kit)](LICENSE)

Expose ASP.NET Core minimal APIs as MCP tools, with the safety rails an agent-facing API needs. Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

Work in progress. Endpoints marked with `.AsMcpTool()` are served as tools today, with input schemas built from their route, query and body types. Read-only defaults, authorization and confirmation for write tools are next. I'm writing it up as a series: [part 1](https://softvasco.github.io/2026/10/when-an-ai-agent-calls-your-dotnet-api/) lists the problems.

## Why

Most of the APIs that an AI agent should be able to call already exist. They have route handlers, request types, validation, auth, and a team that owns them. Writing a second set of tool classes by hand for MCP means two definitions of the same operation that drift apart, and tools that quietly skip the auth and validation the HTTP endpoint had.

McpKit reads what ASP.NET Core already knows about an endpoint (route, parameters, request type, metadata) and publishes it as an MCP tool. Nothing is exposed unless the endpoint opts in. A tool call runs the endpoint's own request delegate in-process, so binding, filters and validation are the same as over HTTP, and errors come back as the endpoint's problem details. Until McpKit checks authorization itself, it refuses to run endpoints that require it.

## How it fits

```mermaid
flowchart LR
    agent[MCP client<br/>VS Code, Cursor, custom] -->|streamable HTTP| mcp[/mcp endpoint]
    subgraph host[ASP.NET Core host]
        mcp --> kit[McpKit<br/>tools from endpoint metadata]
        kit --> api[minimal API endpoints]
        api --> svc[your services]
    end
```

## Quickstart

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpKit();

var app = builder.Build();

app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => Results.Ok(new { id, deposit.Amount }))
    .WithName("deposit")
    .WithSummary("Deposit money into an account")
    .AsMcpTool();

app.MapMcpKit();
app.Run();

record Deposit(decimal Amount, string Currency);
```

The tool is called `deposit`, and its input schema lists `id`, `amount` and `currency`, all required because none of them has a default. Tools written by hand with the SDK still work: `AddMcpKit()` returns the SDK's builder, so `.WithTools<T>()` is there too.

Headers that a filter reads from the request don't show up in a handler's parameters, so declare them. With this, the `deposit` tool gets an optional `idempotency_key` argument and the endpoint sees it as the `Idempotency-Key` header:

```csharp
    .AsMcpTool()
    .WithMcpToolHeader("Idempotency-Key", "idempotency_key",
        "A new value for each deposit. Send the same value again if you retry it, so it runs once.");
```

`AddMcpKit` registers the MCP server with the streamable HTTP transport, stateless by default so the host scales out like any other API. `MapMcpKit` maps it at `/mcp` (change it with `options.Path`). The sample in [samples/McpKit.Sample](samples/McpKit.Sample) has one hand-written tool that returns the server time.

To run it and connect from any MCP client:

```bash
dotnet run --project samples/McpKit.Sample
```

The tests connect to the sample through the official client over `WebApplicationFactory`, so the whole path from HTTP request to tool result runs in-process without an LLM.

## Design notes

Decisions are recorded in [docs/adr](docs/adr/README.md).

## License

MIT
