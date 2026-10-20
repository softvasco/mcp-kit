# 6. Confirmation before a write, with a token bound to the call

Date: 2026-10-21

## Status

Accepted

## Context

Some writes should not run the moment a model decides to call them: a transfer, closing an account, anything a person would want to look at first. Read-only by default (ADR 0004) and per-tool authorization (ADR 0005) decide whether a tool can be called at all. They say nothing about a single call.

MCP has elicitation, where the server asks the client to put a question to the user. It needs a stateful session, because the server sends a request back to the client in the middle of a tool call. McpKit is stateless by default so the host scales out like any other API, and many clients don't support elicitation yet.

So the confirmation has to work in two plain tool calls, with no state on the server between them.

## Decision

- `.RequireMcpConfirmation()` on an endpoint adds an optional `confirmation_token` argument to its tool. A call without it runs nothing. It returns `confirmation_required`, the arguments as the server read them, a token and when the token expires (5 minutes unless the endpoint says otherwise).
- A second call with the same arguments and the token runs the endpoint as usual. The token is dropped before the endpoint sees the arguments.
- The token is the tool name, the caller's id (name identifier, `sub` or name), a SHA-256 of the arguments and the expiry, sealed with ASP.NET Core data protection. Nothing is stored. Any other tool, caller or argument value makes it `tool.confirmation_invalid`, and an old one is `tool.confirmation_expired`. Both are tool errors and the endpoint doesn't run.
- The first call is not an error. It did what it should, which is to wait.

## Consequences

- The model can't preview a deposit of 25 and then run one of 2500 with the same token. What the user was shown is what runs.
- Nothing stops a model from confirming on its own. The token makes the step explicit and gives the client a point to ask the user; it is not proof that a person said yes. A host that needs that proof should use elicitation on a stateful server or confirm outside the agent.
- Data protection keys must be shared between instances, or a token issued by one is invalid on another. In production that means a key ring in shared storage (blob, Redis, a database), the same setup cookie auth already needs.
- Arguments are compared as compact JSON after sorting the keys, so `{"a":1,"b":2}` and `{"b":2,"a":1}` match, but `25` and `25.0` do not.
- Confirmation runs after the SDK's authorization filter, so a caller who may not use the tool never gets a token for it.
