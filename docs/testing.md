# Testing MCP servers

None of the tests in this repo call an LLM. The model only picks a tool and fills in its arguments. Everything after that is code: the schema it read, the checks on the call, the endpoint, and the error it gets back. All of that can be tested with the official MCP client talking to the real host in-process.

`McpKit.Testing` has the three pieces I kept copying between test classes.

## Connecting

`McpTestClient.ConnectAsync` takes the in-memory `HttpClient` from `WebApplicationFactory` or `TestServer` and does the MCP handshake over streamable HTTP. No port is opened.

```csharp
await using var host = new WebApplicationFactory<AccountReadModel>();
await using var client = await McpTestClient.ConnectAsync(host.CreateClient(), cancellationToken: Token);
```

The path defaults to `/mcp`. The MCP client owns the `HttpClient` you pass, so disposing the client is enough.

## Calling a tool

`CallToolForJsonAsync` returns the tool's structured content, or its text parsed as JSON:

```csharp
var result = await client.CallToolForJsonAsync("get_statement", arguments, Token);
```

A tool error throws `McpToolCallException` with the text the model would have seen, so a failing test says why. To test the error itself, assert on the exception, or use the SDK's `CallToolAsync` and read `IsError` and the content:

```csharp
var refused = await client.CallToolAsync("get_balance", account, cancellationToken: Token);

Assert.True(refused.IsError);
var body = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text).RootElement;
Assert.Equal("tool.rate_limited", body.GetProperty("code").GetString());
```

Check the side effect too. The rate limit test counts calls inside the endpoint and asserts the count stayed at 2, because a refusal that still runs the handler looks the same from the outside.

## Pinning the tool list

A renamed argument or a reworded description changes what every agent sees, and nothing else in the build notices. `McpToolSnapshot.MatchAsync` compares `tools/list` with a file in the repo:

```csharp
await McpToolSnapshot.MatchAsync(client, "ledger-sample", Token);
```

The file is `Snapshots/ledger-sample.tools.json`, next to the test. On a difference, or when the file doesn't exist yet, the test fails and writes `ledger-sample.tools.received.json` beside it. Review that file, and if the change is intended, copy it over the snapshot. The diff then shows up in the pull request.

The path comes from `[CallerFilePath]`, so the source has to be where it was compiled. That holds when CI builds and tests in one job. It won't if you build with `PathMap` or run the tests from a separate artifact.

## What to cover

| What can go wrong | How to test it |
|---|---|
| Schema changes without anyone noticing | `McpToolSnapshot` |
| Bad arguments reach the endpoint | Build calls with missing or mistyped values from each tool's own schema, and expect `tool.invalid_arguments` |
| A write tool is exposed by accident | Connect with writes off and assert the tool isn't listed |
| A caller without the scope gets through | Connect as that caller, expect the tool to be missing and a direct call to fail |
| A retry applies twice | Call twice with the same key, check the handler ran once |
| Time-based rules | `FakeTimeProvider` from DI, move the clock, assert the exact wait |

## What this doesn't tell you

These tests say the tools behave. They don't say a model will choose the right one, or understand your description. That needs runs against real models, with their cost and variance, and I keep it out of the normal build.
