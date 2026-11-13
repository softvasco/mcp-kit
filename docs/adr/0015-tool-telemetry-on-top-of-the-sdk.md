# 15. Tool call telemetry on top of the SDK's, not next to it

Date: 2026-11-13

## Status

Accepted

## Context

A host running McpKit wants to see its tool calls in the same place as the rest of its telemetry: how many, how long, which ones fail. The MCP C# SDK (2.2) already does part of this. It starts a span for every request under the `Experimental.ModelContextProtocol` source, with `mcp.method.name` and `gen_ai.tool.name`, and records `mcp.server.operation.duration`.

What the SDK can't see is what McpKit decided. A call that hit the rate limit, one that is waiting for a confirmation and one that ran and got a 422 all come back to the SDK as a normal `CallToolResult`, so its span and its histogram treat them the same. Those are the numbers an operator asks about first: is an agent looping into the limit, are write calls being confirmed.

## Decision

- No second span per call. The outcome and the endpoint's status go on the current span as `mcpkit.tool.outcome` and `http.response.status_code`. When the host traces the SDK's source, that span is the `tools/call` one; when it doesn't, the tags land on whatever span is current, usually the HTTP request.
- A meter named `McpKit` (`McpKitTelemetry.MeterName`) with two instruments, both tagged with `gen_ai.tool.name` and `mcpkit.tool.outcome`:
  - `mcpkit.tool.calls`, a counter of calls.
  - `mcpkit.tool.duration`, a histogram in seconds.
- Outcomes are the audit's (ADR 0008) in snake_case: `succeeded`, `failed`, `confirmation_requested`, `confirmation_refused`, `rate_limited`.
- Both are recorded from the same `ToolCallRecord` the audit gets, so the duration comes from the host's `TimeProvider` and the two can't disagree.
- The meter comes from `IMeterFactory`, so tests can collect it per host and nothing is static.

## Consequences

- The tool name tag uses the GenAI semantic convention name, so McpKit's metrics and the SDK's spans group by the same key.
- The caller is not a metric tag. It would make one series per client, and the audit already has it.
- The SDK's source name still starts with `Experimental`. A host has to add it with `AddSource` to get the spans, and it may change in a later SDK version; McpKit doesn't depend on the name.
- Hosts opt in with `AddMeter(McpKitTelemetry.MeterName)`. Nothing is exported unless they do.
