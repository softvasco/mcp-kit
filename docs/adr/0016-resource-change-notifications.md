# 16. Resource change notifications, sent when the host says so

Date: 2026-11-15

## Status

Accepted

## Context

A client that attached a resource, say an account, shows what it read at that moment. MCP lets it subscribe to the URI (`resources/subscribe`) and the server sends `notifications/resources/updated` when it changes, so the client can read it again.

Two things make this harder than it looks. First, McpKit doesn't know when data changes: a resource is a GET endpoint, and the write that changes it may come from a tool call, a normal HTTP request, a background job or another service. Second, McpKit is stateless by default (no session id), and a notification needs a session to go out on later.

## Decision

- The host tells McpKit what changed. `IMcpResourceNotifier.NotifyUpdatedAsync(uri)` is registered by `AddMcpKit`, and the host calls it after the write, with the URI as clients read it (`api://accounts/42`). Guessing from write tools (a POST to `/accounts/{id}/deposits` probably changes `/accounts/{id}`) would miss every change that doesn't go through MCP and fire for writes that failed half-way.
- Subscriptions only exist on a stateful server. With `Stateless = true` the server doesn't advertise `subscribe`, the SDK refuses the request, and `NotifyUpdatedAsync` has no one to notify, so the host can call it either way.
- A subscription is for an exact URI, and the URI has to match one of the resource templates, otherwise the client gets `ResourceNotFound`. No wildcards: the spec doesn't define them.
- Subscriptions live in memory per session, keyed by session id. The subscribe and unsubscribe handlers only see a wrapper bound to their own request, a new one each time, so keying by that object meant an unsubscribe never found its entry. McpKit runs each stateful session through the transport's `RunSessionHandler`, keeps that server for sending, and drops the session's subscriptions when it ends.

## Consequences

- A host has to call the notifier from its write paths. When a resource reads a projection, the place to call it is where the projection is updated, not where the command ran.
- Subscriptions are per server instance. A host scaled out to several instances needs sticky sessions and has to deliver each change to every instance, for example through the bus it already has. McpKit doesn't do that part.
- `RunSessionHandler` is marked experimental in SDK 2.2.0 (MCPEXP002), so the warning is suppressed at that one call. If the SDK changes it, the subscriptions tests fail first.
- The notification carries only the URI, as the spec says. The client reads the resource again to get the new content, through the same authorization and redaction as before.
