# 11. Upstream credentials are picked per caller, never passed through

Date: 2026-11-06

## Status

Accepted

## Context

OpenAPI tools (the `McpKit.OpenApi` package) call an API that runs somewhere else. That API usually wants a token, and the call should be made as the agent's user, not as one service account that can see everyone's data.

The obvious shortcut is to take the bearer token the MCP client sent and put it on the upstream request. The MCP authorization spec rules that out: an MCP server must not pass through the token it received from the client, and must only accept tokens issued for itself. A token issued for the MCP server, sent to another API, either fails there or works because that API doesn't check its audience, which is worse. It also hides who really called the upstream from its logs and rate limits.

There is a second way in for a foreign token: an operation can declare `Authorization` as a header parameter, and ADR 0003 sends header arguments as headers, so the model itself could fill it in.

## Decision

- `WithUpstreamCredentials(name, credentials)` registers a delegate for one OpenAPI API. For every call it gets the MCP caller (`ClaimsPrincipal`) and the host's services, and returns the `Authorization` header to send, or null for none.
- The delegate is where the host does a token exchange (RFC 8693, or Entra ID's on-behalf-of flow), looks up a per-tenant key, or returns a fixed key for an API that has no users. McpKit doesn't ship any of those flows.
- It runs as a `DelegatingHandler` on the API's named `HttpClient`, and the caller travels on `HttpRequestMessage.Options`. The handler sets the header last, so it replaces any `Authorization` a header argument put there.
- Without the delegate nothing changes: the upstream gets only what the configured `HttpClient` adds, and the client's token never leaves the MCP server.

## Consequences

- A host that wants per-user calls has to have a way to get tokens for the upstream: an identity provider that supports token exchange, or its own mapping. That is the cost of not forwarding the client's token.
- The delegate runs on every call. Caching exchanged tokens until they expire is up to the host.
- A call with no authenticated user still reaches the delegate, with an empty principal, so the host decides whether anonymous calls get a key.
- The handler's services come from the `HttpClient` handler scope, not the MCP request scope, so the delegate should only use singletons.
