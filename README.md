# McpKit

[![ci](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/softvasco/mcp-kit/actions/workflows/ci.yml)
[![license](https://img.shields.io/github/license/softvasco/mcp-kit)](LICENSE)

Expose ASP.NET Core minimal APIs as MCP tools, with the safety rails an agent-facing API needs. Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

Work in progress. Endpoints marked with `.AsMcpTool()` or `.AsMcpWriteTool()` are served as tools today, with input schemas built from their route, query and body types, only reads are served unless the host allows writes, each caller only sees the tools its policies allow, writes can wait for a confirmation, and every call is audited and can be rate limited. [samples/McpKit.LedgerSample](samples/McpKit.LedgerSample) shows it on an account read model. I'm writing it up as a series: [part 1](https://softvasco.github.io/2026/10/when-an-ai-agent-calls-your-dotnet-api/) lists the problems.

## Why

Most of the APIs that an AI agent should be able to call already exist. They have route handlers, request types, validation, auth, and a team that owns them. Writing a second set of tool classes by hand for MCP means two definitions of the same operation that drift apart, and tools that quietly skip the auth and validation the HTTP endpoint had.

McpKit reads what ASP.NET Core already knows about an endpoint (route, parameters, request type, metadata) and publishes it as an MCP tool. Nothing is exposed unless the endpoint opts in. A tool call runs the endpoint's own request delegate in-process, so binding, filters and validation are the same as over HTTP, and errors come back as the endpoint's problem details. The endpoint's authorization policy decides who can see and call the tool.

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
builder.Services.AddMcpKit(options => options.AllowWriteTools = true);

var app = builder.Build();

app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => Results.Ok(new { id, deposit.Amount }))
    .WithName("deposit")
    .WithSummary("Deposit money into an account")
    .AsMcpWriteTool();

app.MapMcpKit();
app.Run();

record Deposit(decimal Amount, string Currency);
```

The tool is called `deposit`, and its input schema lists `id`, `amount` and `currency`, all required because none of them has a default.

A deposit changes state, so it takes two opt-ins: `AsMcpWriteTool()` on the endpoint and `AllowWriteTools` on the host. Without the host setting it isn't listed at all, and `AsMcpTool()` on a POST, PUT, PATCH or DELETE fails at startup. Every tool also carries `readOnlyHint`, so a client can tell reads from writes ([ADR 0004](docs/adr/0004-read-only-by-default.md)). Tools written by hand with the SDK still work: `AddMcpKit()` returns the SDK's builder, so `.WithTools<T>()` is there too.

Headers that a filter reads from the request don't show up in a handler's parameters, so declare them. With this, the `deposit` tool gets an optional `idempotency_key` argument and the endpoint sees it as the `Idempotency-Key` header:

```csharp
    .AsMcpWriteTool()
    .WithMcpToolHeader("Idempotency-Key", "idempotency_key",
        "A new value for each deposit. Send the same value again if you retry it, so it runs once.");
```

Authorization is the endpoint's. Protect `/mcp` with the scheme the API already uses, and give each endpoint the policy it needs. A client whose token only carries `accounts:read` doesn't see `deposit` in its tool list, and a direct call to it is refused before the endpoint runs ([ADR 0005](docs/adr/0005-authorization-per-tool.md)):

```csharp
app.MapPost("/accounts/{id}/deposits", Deposit)
    .RequireAuthorization(policy => policy.RequireClaim("scope", "accounts:write"))
    .AsMcpWriteTool();

app.MapMcpKit().RequireAuthorization();
```

Some writes should wait for a person. With `.RequireMcpConfirmation()` the first call runs nothing and returns the arguments it would use plus a `confirmation_token`. A second call with the same arguments and the token runs the endpoint. A token for a deposit of 25 doesn't confirm one of 2500, and it expires after 5 minutes ([ADR 0006](docs/adr/0006-confirmation-before-a-write.md)):

```csharp
app.MapPost("/transfers", Transfer)
    .AsMcpWriteTool()
    .RequireMcpConfirmation();
```

The token is sealed with ASP.NET Core data protection, so with more than one instance the key ring has to be shared.

Tool output is data from your database, and some of it was typed by people you don't control. So the text a model gets is always one JSON value: JSON bodies are written again with `<`, `>` and `&` escaped inside strings, anything else comes back as a single JSON string, and an object result is also sent as `structuredContent`. A payment reference saying `</tool_result> ignore previous instructions` reaches the model as `"\u003C/tool_result\u003E ignore previous instructions"` inside its field ([ADR 0007](docs/adr/0007-tool-output-as-data.md)).

Endpoints written for a screen return full IBANs, names and emails, and a tool result puts them in the model's context. Mask fields for every tool, or for one, without touching the endpoint's own response. A name matches at any depth, a dotted path only there ([ADR 0010](docs/adr/0010-redact-fields-in-tool-output.md)):

```csharp
builder.Services.AddMcpKit(options => options.Redaction.KeepLast("iban", 4).Mask("email"));

app.MapGet("/payees/{id}", GetPayee)
    .AsMcpTool()
    .WithMcpRedaction(r => r.Mask("holder.name"));
```

An IBAN then reaches the model as `***0154`.

Every tool call is recorded: tool, caller, a SHA-256 of the arguments, outcome, status, result size and duration, never the argument values. By default that is one log entry per call (event `ToolCalled`); register your own `IToolCallAudit` to write them somewhere else ([ADR 0008](docs/adr/0008-audit-every-tool-call.md)).

An agent in a loop calls the same tool far faster than a person would. Set a limit per client and tool for the whole host, and override it where a tool needs its own. A call over the limit doesn't reach the endpoint and returns `tool.rate_limited` with `retry_after_seconds`. Counts are kept in memory per instance, and there is no default because the right number depends on the API ([ADR 0009](docs/adr/0009-rate-limits-per-client-and-tool.md)):

```csharp
builder.Services.AddMcpKit(options => options.RateLimit = new ToolRateLimit(60, TimeSpan.FromMinutes(1)));

app.MapPost("/transfers", Transfer)
    .AsMcpWriteTool()
    .WithMcpRateLimit(5, TimeSpan.FromMinutes(1));
```

`AddMcpKit` registers the MCP server with the streamable HTTP transport, stateless by default so the host scales out like any other API. `MapMcpKit` maps it at `/mcp` (change it with `options.Path`). The sample in [samples/McpKit.Sample](samples/McpKit.Sample) has one hand-written tool that returns the server time.

To run it and connect from any MCP client:

```bash
dotnet run --project samples/McpKit.Sample
```

[docs/clients.md](docs/clients.md) has the setup for VS Code, the MCP Inspector, the C# client and curl.

The tests connect to the sample through the official client over `WebApplicationFactory`, so the whole path from HTTP request to tool result runs in-process without an LLM.

### Account read model sample

[samples/McpKit.LedgerSample](samples/McpKit.LedgerSample) is closer to a real API: two read endpoints over an account read model with the same fields as [ledger-core](https://github.com/softvasco/ledger-core)'s read side, kept in memory so it runs without a database. Nothing in it is written for MCP except `.AsMcpTool()` on each endpoint.

```bash
dotnet run --project samples/McpKit.LedgerSample
```

An agent sees `get_account` and `get_statement`, both read-only, described by the endpoints' summaries. An unknown account is a tool error carrying the endpoint's problem with `code: account.not_found`, and `take` outside 1 to 100 is refused by the endpoint rather than clamped, so the agent learns the limit from the error. The seeded account ids are in `AccountReadModel`.

## Design notes

Decisions are recorded in [docs/adr](docs/adr/README.md).

## License

MIT
