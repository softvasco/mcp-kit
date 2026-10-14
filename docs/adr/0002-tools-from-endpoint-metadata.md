# 2. Tools from endpoint metadata, called in-process

Date: 2026-10-14

## Status

Accepted

## Context

The point of McpKit is that an existing minimal API endpoint becomes an MCP tool without a second, hand-written definition. Two questions decide most of the design: how an endpoint says it wants to be a tool, and how a tool call reaches the endpoint.

For the second one I looked at three options:

| Option | Runs | Cost |
|---|---|---|
| Loopback HTTP call to the host's own URL | the whole pipeline, including middleware and auth | a network hop, and the host has to know its own public address, which breaks behind proxies and in tests |
| Call the endpoint's `RequestDelegate` with a new `HttpContext` | route binding, endpoint filters, the handler, typed results | middleware is skipped, so authorization must be checked by McpKit itself |
| Call the handler method directly by reflection | the handler only | no binding, no filters, no validation: exactly the drift this library exists to avoid |

## Decision

- An endpoint opts in with `.AsMcpTool()` on its convention builder, which adds `McpToolMetadata`. Nothing else is exposed. The tool name defaults to the endpoint name (`WithName`) in snake_case, and the description to its summary or description metadata.
- An opted-in endpoint without a name is a startup error, not a silently skipped tool. Tool names are what an agent sees and what an allow-list refers to, so they must be stable and chosen by a person.
- Discovery reads `EndpointDataSource`, so it sees endpoints from route groups and any other source the host maps.
- A tool call builds an `HttpContext` in the caller's scope, with the caller's user, fills route values, query and body from the tool arguments, and invokes the endpoint's `RequestDelegate`. Binding, endpoint filters and validation run exactly as for an HTTP request.
- Because middleware is skipped, McpKit checks the endpoint's authorization metadata itself with `IAuthorizationService` before invoking it. That lands with the auth work; until then a tool only runs endpoints with no authorization metadata, and refuses the rest.

## Consequences

- One definition per operation. Changing the endpoint changes the tool.
- Middleware that changes behaviour (rate limiting, custom headers) does not run for tool calls. Endpoint filters do, so cross-cutting rules that must apply to tools belong in filters.
- The response is read from the in-memory body the endpoint wrote, including problem details, so a refused command comes back to the agent with the same `code` an HTTP client gets.
