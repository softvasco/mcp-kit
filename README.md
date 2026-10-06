# McpKit

[![ci](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml)
[![license](https://img.shields.io/github/license/softvasco/mcp-kit)](LICENSE)

Expose ASP.NET Core minimal APIs as MCP tools, with the safety rails an agent-facing API needs. Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

Work in progress. Today it is a thin host and a test harness; turning existing endpoints into tools is next.

## Why

Most of the APIs that an AI agent should be able to call already exist. They have route handlers, request types, validation, auth, and a team that owns them. Writing a second set of tool classes by hand for MCP means two definitions of the same operation that drift apart, and tools that quietly skip the auth and validation the HTTP endpoint had.

McpKit reads what ASP.NET Core already knows about an endpoint (route, parameters, request type, metadata) and publishes it as an MCP tool. Nothing is exposed unless the endpoint opts in, tools are read-only unless marked otherwise, and the tool goes through the same authorization as the endpoint.

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
builder.Services.AddMcpKit().WithTools<ClockTools>();

var app = builder.Build();
app.MapMcpKit();
app.Run();
```

`AddMcpKit` registers the MCP server with the streamable HTTP transport, stateless by default so the host scales out like any other API. `MapMcpKit` maps it at `/mcp` (change it with `options.Path`). The sample in [samples/McpKit.Sample](samples/McpKit.Sample) is exactly this, with one tool that returns the server time.

To run it and connect from any MCP client:

```bash
dotnet run --project samples/McpKit.Sample
```

The tests connect to the sample through the official client over `WebApplicationFactory`, so the whole path from HTTP request to tool result runs in-process without an LLM.

## Design notes

Decisions are recorded in [docs/adr](docs/adr/README.md).

## License

MIT
