# 5. Authorization per tool comes from the endpoint's policy

Date: 2026-10-20

## Status

Accepted

## Context

Until now McpKit refused to run any endpoint with authorization metadata. A tool call runs the endpoint's request delegate in-process (ADR 0002), and that skips the authorization middleware, so a protected endpoint would have run for anyone who could reach `/mcp`.

The issue asked for auth per client and a scope per tool. The client part already exists in ASP.NET Core: `/mcp` is an endpoint like any other, so the host protects it with whatever scheme it uses for its API (JWT bearer, an API key handler) through `MapMcpKit().RequireAuthorization()`. What was missing is the per-tool part.

I didn't want a second permission model for tools. The endpoint already says who may call it, with `RequireAuthorization("policy")`, `RequireAuthorization(p => p.RequireClaim("scope", "accounts:write"))` or `[Authorize]`. A tool should need exactly that.

## Decision

- Before a call, the invoker combines the endpoint's `IAuthorizeData` and `AuthorizationPolicy` metadata into one policy, the way the authorization middleware does, and checks it against the MCP caller's user with `IAuthorizationService`. `AllowAnonymous` wins, as it does over HTTP.
- A refused call never reaches the endpoint. The caller gets a problem with `tool.unauthenticated` (401) when there is no signed-in user and `tool.forbidden` (403) when the user lacks the policy.
- The tool list is filtered per caller. Each tool exposes its endpoint's metadata to the SDK, and `AddMcpKit` turns on the SDK's authorization filters, so a client only sees the tools its policies allow. Calling a hidden tool fails at the protocol level, like calling a tool that doesn't exist.

## Consequences

- Scopes are claims, so a token issued for `accounts:read` gets the read tools and nothing else. Nothing in McpKit knows what a scope is.
- The policy's authentication schemes are not used. The user is whoever `/mcp` authenticated, so an endpoint that accepts a different scheme over HTTP gets the MCP caller instead.
- The check runs twice for a listed tool: once in the SDK filter and once in the invoker. The invoker's check is the one that holds when someone calls `EndpointToolInvoker` directly.
- Resource-based checks inside the handler (is this the caller's account?) still run, because the handler runs as usual.
