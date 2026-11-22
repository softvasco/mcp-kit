# Performance

How much a tool call costs on top of calling the endpoint directly. McpKit runs the endpoint's own `RequestDelegate` on a `DefaultHttpContext` it builds in memory (no second HTTP request), so the extra work is the MCP side: a JSON-RPC POST, the response written as an SSE event, binding the tool arguments and wrapping the endpoint's body as tool output.

## Load test

[benchmarks/McpKit.LoadTest](../benchmarks/McpKit.LoadTest) points N clients at a running host. Each client makes the same number of calls one after another, first as a plain `GET /accounts/{id}`, then as the `get_account` tool over its own MCP session (the way separate agents would connect). Both paths reach the same endpoint and the same in-memory read model.

```bash
dotnet run -c Release --project samples/McpKit.LedgerSample --urls http://localhost:5199
dotnet run -c Release --project benchmarks/McpKit.LoadTest -- --clients 1,8,32,64 --calls 2000 --warmup 200
```

Results from 2026-11-22: .NET 10.0, Release, stateless server (the default), AMD Ryzen 7 7840HS with 16 logical cores, client and server on the same machine over loopback. 2000 calls per client after 200 warm-up calls each.

| Clients | Path | Calls/s | p50 ms | p95 ms | p99 ms |
|---:|---|---:|---:|---:|---:|
| 1 | HTTP GET | 7233 | 0.13 | 0.18 | 0.21 |
| 1 | MCP get_account | 2025 | 0.48 | 0.57 | 0.64 |
| 8 | HTTP GET | 16985 | 0.43 | 0.94 | 1.23 |
| 8 | MCP get_account | 13669 | 0.57 | 0.72 | 1.61 |
| 32 | HTTP GET | 16232 | 1.82 | 3.54 | 4.62 |
| 32 | MCP get_account | 10452 | 2.86 | 4.84 | 6.40 |
| 64 | HTTP GET | 14666 | 4.11 | 6.77 | 8.69 |
| 64 | MCP get_account | 11024 | 5.73 | 8.27 | 9.74 |

## Reading the numbers

- With one client a tool call takes about 0.35 ms more than the plain GET at the median. A model takes hundreds of milliseconds to decide on the next call, so for an agent this is noise.
- From 8 clients on, the MCP path does 64 to 80% of the plain calls per second. Past 8 clients neither path goes faster: the load generator runs on the same 16 cores as the server, so these rows show where this one machine runs out, not a limit of the host.
- The runs move by 10 to 20% from one to the next. A shorter warm-up (50 calls) put the single-client MCP median at 1 ms, because the tool path was still being JIT compiled, so use at least a few hundred warm-up calls.

## Not measured yet

- A real database behind the endpoint. With the read model in memory the endpoint costs almost nothing, so this table shows McpKit's overhead at its largest share of a call.
- Stateful sessions (`Stateless = false`), which are needed for resource subscriptions.
- Client and server on separate machines.
